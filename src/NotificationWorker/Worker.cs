using System.Diagnostics;
using System.Text.Json;
using Confluent.Kafka;
using OpenTelemetry.Trace;
using NotificationWorker.Contracts;
using NotificationWorker.Data;
using NotificationWorker.Extensions;
using NotificationWorker.Metrics;
using NotificationWorker.Messaging;

namespace NotificationWorker;

public sealed class Worker(
    ILogger<Worker> logger,
    IConfiguration configuration,
    IConsumer<string, string> consumer,
    IServiceScopeFactory serviceScopeFactory,
    INotificationMetrics metrics,
    NotificationLagRefresher lagRefresher) : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new(OtelExtensions.ActivitySourceName);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _notificationsTopic = configuration["KAFKA_TOPIC_NOTIFICATIONS"] ?? "notifications";
    private readonly string _consumerGroupId = configuration["KAFKA_GROUP_ID_NOTIFICATION"] ?? "notification-worker";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        consumer.Subscribe(_notificationsTopic);

        logger.LogInformation(
            "Notification worker subscribed to Kafka topic {Topic} with group {GroupId}",
            _notificationsTopic,
            _consumerGroupId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> consumeResult;

                try
                {
                    consumeResult = consumer.Consume(stoppingToken);
                    lagRefresher.Refresh(consumer);
                }
                catch (ConsumeException ex)
                {
                    metrics.RecordConsumeFailure($"{ex.Error.Code}:{ex.Error.Reason}");

                    logger.LogError(
                        ex,
                        "Notification consume failed Classification={Classification} Topic={Topic}",
                        "consume_failed",
                        _notificationsTopic);
                    continue;
                }

                try
                {
                    await ProcessMessageAsync(consumeResult, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    metrics.RecordResult(NotificationResults.UnexpectedError);

                    logger.LogError(
                        ex,
                        "Unexpected notification processing failure Classification={Classification} Topic={Topic}",
                        "consume_failed",
                        consumeResult.Topic);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Notification worker cancellation requested. Closing Kafka consumer.");
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task ProcessMessageAsync(ConsumeResult<string, string> consumeResult, CancellationToken cancellationToken)
    {
        var parentContext = KafkaTracingHelper.Extract(consumeResult.Message.Headers);

        using var activity = parentContext is ActivityContext extractedContext
            ? ActivitySource.StartActivity("kafka consume notifications", ActivityKind.Consumer, extractedContext)
            : ActivitySource.StartActivity("kafka consume notifications", ActivityKind.Consumer);

        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", consumeResult.Topic);
        activity?.SetTag("messaging.operation", "receive");
        activity?.SetTag("messaging.consumer.group.name", _consumerGroupId);
        activity?.SetTag("messaging.kafka.message.key", consumeResult.Message.Key);
        activity?.SetTag("messaging.kafka.partition", consumeResult.Partition.Value);
        activity?.SetTag("messaging.kafka.offset", consumeResult.Offset.Value);

        if (parentContext is null)
        {
            logger.LogWarning(
                "Distributed context missing or invalid for consumed notification message Topic={Topic} Key={Key} TraceId={TraceId} SpanId={SpanId}",
                consumeResult.Topic,
                consumeResult.Message.Key,
                activity?.TraceId.ToString(),
                activity?.SpanId.ToString());
        }

        NotificationRequestedEvent? notification;

        try
        {
            notification = JsonSerializer.Deserialize<NotificationRequestedEvent>(consumeResult.Message.Value, SerializerOptions);
        }
        catch (JsonException ex)
        {
            activity?.AddException(ex);
            activity?.SetTag("error.type", "invalid_payload");
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid JSON payload.");

            logger.LogError(
                ex,
                "Invalid notification payload Classification={Classification} Topic={Topic} TraceId={TraceId} SpanId={SpanId} Payload={Payload}",
                "invalid_payload",
                consumeResult.Topic,
                activity?.TraceId.ToString(),
                activity?.SpanId.ToString(),
                consumeResult.Message.Value);

            metrics.RecordResult(NotificationResults.InvalidPayload);

            return;
        }

        if (!TryValidateNotification(notification, out var validationError))
        {
            activity?.SetTag("error.type", "invalid_payload");
            activity?.SetStatus(ActivityStatusCode.Error, validationError);

            logger.LogError(
                "Notification payload validation failed Classification={Classification} Topic={Topic} Key={Key} TraceId={TraceId} SpanId={SpanId} Reason={Reason} Payload={Payload}",
                "invalid_payload",
                consumeResult.Topic,
                consumeResult.Message.Key,
                activity?.TraceId.ToString(),
                activity?.SpanId.ToString(),
                validationError,
                consumeResult.Message.Value);

            metrics.RecordResult(NotificationResults.InvalidPayload);

            return;
        }

        var validNotification = notification!;

        activity?.SetTag("order.id", validNotification.OrderId.ToString());

        logger.LogInformation(
            "Processing notification message OrderId={OrderId} Topic={Topic} TraceId={TraceId} SpanId={SpanId}",
            validNotification.OrderId,
            consumeResult.Topic,
            activity?.TraceId.ToString(),
            activity?.SpanId.ToString());

        var persistedNotification = new PersistedNotification
        {
            Id = Guid.NewGuid(),
            OrderId = validNotification.OrderId,
            Description = validNotification.Description!.Trim(),
            Status = validNotification.Status!.Trim(),
            CreatedAtUtc = validNotification.CreatedAtUtc,
            PublishedAtUtc = validNotification.PublishedAtUtc!.Value,
            ProcessedAtUtc = validNotification.ProcessedAtUtc,
            PersistedAtUtc = DateTimeOffset.UtcNow,
            TraceId = activity?.TraceId.ToString() ?? Activity.Current?.TraceId.ToString() ?? string.Empty
        };

        var persistenceStopwatch = Stopwatch.StartNew();

        try
        {
            using var scope = serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

            dbContext.NotificationResults.Add(persistedNotification);
            await dbContext.SaveChangesAsync(cancellationToken);

            metrics.RecordPersistenceResult(NotificationResults.Persisted, persistenceStopwatch.Elapsed);

            logger.LogInformation(
                "Notification persisted successfully OrderId={OrderId} PersistedAtUtc={PersistedAtUtc} TraceId={TraceId} SpanId={SpanId}",
                persistedNotification.OrderId,
                persistedNotification.PersistedAtUtc,
                activity?.TraceId.ToString(),
                activity?.SpanId.ToString());
        }
        catch (Exception ex)
        {
            metrics.RecordPersistenceResult(NotificationResults.PersistenceFailed, persistenceStopwatch.Elapsed);
            activity?.AddException(ex);
            activity?.SetTag("error.type", "persistence_failed");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            logger.LogError(
                ex,
                "Notification persistence failed Classification={Classification} OrderId={OrderId} TraceId={TraceId} SpanId={SpanId}",
                "persistence_failed",
                persistedNotification.OrderId,
                activity?.TraceId.ToString(),
                activity?.SpanId.ToString());
        }
    }

    private static bool TryValidateNotification(NotificationRequestedEvent? notification, out string validationError)
    {
        if (notification is null)
        {
            validationError = "Notification payload is required.";
            return false;
        }

        if (notification.OrderId == Guid.Empty)
        {
            validationError = "orderId is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(notification.Description))
        {
            validationError = "description is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(notification.Status))
        {
            validationError = "status is required.";
            return false;
        }

        if (notification.CreatedAtUtc == default)
        {
            validationError = "createdAtUtc is required.";
            return false;
        }

        if (notification.ProcessedAtUtc == default)
        {
            validationError = "processedAtUtc is required.";
            return false;
        }

        if (notification.ProcessedAtUtc < notification.CreatedAtUtc)
        {
            validationError = "processedAtUtc cannot be earlier than createdAtUtc.";
            return false;
        }

        if (notification.PublishedAtUtc is null || notification.PublishedAtUtc == default)
        {
            validationError = "publishedAtUtc is required.";
            return false;
        }

        if (string.Equals(notification.Status, "published", StringComparison.OrdinalIgnoreCase)
            && notification.PublishedAtUtc is null)
        {
            validationError = "publishedAtUtc is required when status is published.";
            return false;
        }

        validationError = string.Empty;
        return true;
    }
}