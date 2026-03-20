using System.Diagnostics;
using System.Text.Json;
using Confluent.Kafka;
using OpenTelemetry.Trace;
using ProcessingWorker.Clients;
using ProcessingWorker.Contracts;
using ProcessingWorker.Extensions;
using ProcessingWorker.Metrics;
using ProcessingWorker.Messaging;

namespace ProcessingWorker;

public sealed class Worker(
    ILogger<Worker> logger,
    IConfiguration configuration,
    IConsumer<string, string> consumer,
    IOrderServiceClient orderServiceClient,
    INotificationPublisher notificationPublisher,
    IProcessingMetrics metrics,
    ProcessingLagRefresher lagRefresher) : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new(OtelExtensions.ActivitySourceName);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _ordersTopic = configuration["KAFKA_TOPIC_ORDERS"] ?? "orders";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        consumer.Subscribe(_ordersTopic);

        logger.LogInformation("Processing worker subscribed to Kafka topic {Topic}", _ordersTopic);

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
                    logger.LogError(ex, "Kafka consume failed for topic {Topic}", _ordersTopic);
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
                    logger.LogError(
                        ex,
                        "Unexpected processing failure for consumed message from topic {Topic}",
                        consumeResult.Topic);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Processing worker cancellation requested. Closing Kafka consumer.");
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task ProcessMessageAsync(ConsumeResult<string, string> consumeResult, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = ProcessingResults.UnexpectedError;
        try
        {
            var parentContext = KafkaTracingHelper.Extract(consumeResult.Message.Headers);

            using var activity = parentContext is ActivityContext extractedContext
                ? ActivitySource.StartActivity("kafka consume orders", ActivityKind.Consumer, extractedContext)
                : ActivitySource.StartActivity("kafka consume orders", ActivityKind.Consumer);

            activity?.SetTag("messaging.system", "kafka");
            activity?.SetTag("messaging.destination.name", consumeResult.Topic);
            activity?.SetTag("messaging.operation", "receive");
            activity?.SetTag("messaging.kafka.message.key", consumeResult.Message.Key);
            activity?.SetTag("messaging.kafka.partition", consumeResult.Partition.Value);
            activity?.SetTag("messaging.kafka.offset", consumeResult.Offset.Value);

            if (parentContext is null)
            {
                logger.LogWarning(
                    "Distributed context missing or invalid for consumed order message {Topic} {Key} {TraceId} {SpanId}",
                    consumeResult.Topic,
                    consumeResult.Message.Key,
                    activity?.TraceId.ToString(),
                    activity?.SpanId.ToString());
            }

            ProcessingOrderCreatedEvent? orderEvent;

            try
            {
                orderEvent = JsonSerializer.Deserialize<ProcessingOrderCreatedEvent>(consumeResult.Message.Value, SerializerOptions);
            }
            catch (JsonException ex)
            {
                result = ProcessingResults.InvalidPayload;
                activity?.AddException(ex);
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid JSON payload.");

                logger.LogError(
                    ex,
                    "Invalid JSON payload consumed from topic {Topic} {TraceId} {SpanId} Payload={Payload}",
                    consumeResult.Topic,
                    activity?.TraceId.ToString(),
                    activity?.SpanId.ToString(),
                    consumeResult.Message.Value);

                metrics.RecordProcessingResult(result, stopwatch.Elapsed);

                return;
            }

            if (orderEvent is null || orderEvent.OrderId == Guid.Empty)
            {
                result = ProcessingResults.InvalidPayload;
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid order payload.");

                logger.LogError(
                    "Consumed message without a valid orderId from topic {Topic} {TraceId} {SpanId} Payload={Payload}",
                    consumeResult.Topic,
                    activity?.TraceId.ToString(),
                    activity?.SpanId.ToString(),
                    consumeResult.Message.Value);

                metrics.RecordProcessingResult(result, stopwatch.Elapsed);

                return;
            }

            activity?.SetTag("order.id", orderEvent.OrderId.ToString());

            logger.LogInformation(
                "Processing order message {OrderId} from topic {Topic} {TraceId} {SpanId}",
                orderEvent.OrderId,
                consumeResult.Topic,
                activity?.TraceId.ToString(),
                activity?.SpanId.ToString());

            var orderLookup = await orderServiceClient.GetOrderAsync(orderEvent.OrderId, cancellationToken);

            if (!HandleLookupOutcome(orderEvent, orderLookup, activity, ref result))
            {
                metrics.RecordProcessingResult(result, stopwatch.Elapsed);
                return;
            }

            var order = orderLookup.Order!;
            var notification = new NotificationRequestedEvent(
                order.OrderId,
                order.Description,
                order.Status,
                order.CreatedAtUtc,
                order.PublishedAtUtc!.Value,
                DateTimeOffset.UtcNow);

            try
            {
                await notificationPublisher.PublishAsync(notification, cancellationToken);
                result = ProcessingResults.Processed;

                logger.LogInformation(
                    "Published notification message for order {OrderId} {TraceId} {SpanId}",
                    notification.OrderId,
                    activity?.TraceId.ToString(),
                    activity?.SpanId.ToString());
            }
            catch (Exception ex)
            {
                result = ProcessingResults.PublishFailed;
                activity?.AddException(ex);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

                logger.LogError(
                    ex,
                    "Failed to publish notification message for order {OrderId} {TraceId} {SpanId}",
                    notification.OrderId,
                    activity?.TraceId.ToString(),
                    activity?.SpanId.ToString());
            }

            metrics.RecordProcessingResult(result, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            metrics.RecordProcessingResult(result, stopwatch.Elapsed);
            throw;
        }
    }

    private bool HandleLookupOutcome(
        ProcessingOrderCreatedEvent orderEvent,
        OrderLookupResult orderLookup,
        Activity? activity,
        ref string result)
    {
        switch (orderLookup.Status)
        {
            case OrderLookupStatus.Success:
                break;

            case OrderLookupStatus.NotFound:
                result = ProcessingResults.NotFound;
                activity?.SetTag("http.status_code", 404);
                activity?.SetTag("error.type", "order_not_found");
                activity?.SetStatus(ActivityStatusCode.Error, "Order not found.");

                logger.LogWarning(
                    "Order enrichment returned 404 for order {OrderId} {TraceId} {SpanId}",
                    orderEvent.OrderId,
                    activity?.TraceId.ToString(),
                    activity?.SpanId.ToString());

                return false;

            case OrderLookupStatus.HttpError:
                result = ProcessingResults.HttpError;
                activity?.SetTag("http.status_code", orderLookup.HttpStatusCode);
                activity?.SetTag("error.type", "http_error");
                activity?.SetStatus(ActivityStatusCode.Error, orderLookup.ErrorMessage ?? "HTTP error during enrichment.");

                logger.LogError(
                    "Order enrichment failed with HTTP {StatusCode} for order {OrderId} {TraceId} {SpanId}",
                    orderLookup.HttpStatusCode,
                    orderEvent.OrderId,
                    activity?.TraceId.ToString(),
                    activity?.SpanId.ToString());

                return false;

            case OrderLookupStatus.Timeout:
                result = ProcessingResults.Timeout;
                activity?.SetTag("error.type", "timeout");
                activity?.SetStatus(ActivityStatusCode.Error, orderLookup.ErrorMessage ?? "Order enrichment timed out.");

                logger.LogError(
                    "Order enrichment timed out for order {OrderId} {TraceId} {SpanId}",
                    orderEvent.OrderId,
                    activity?.TraceId.ToString(),
                    activity?.SpanId.ToString());

                return false;

            case OrderLookupStatus.NetworkError:
                result = ProcessingResults.NetworkError;
                activity?.SetTag("error.type", "network_error");
                activity?.SetStatus(ActivityStatusCode.Error, orderLookup.ErrorMessage ?? "Network failure during enrichment.");

                logger.LogError(
                    "Order enrichment failed due to network error for order {OrderId} {TraceId} {SpanId}",
                    orderEvent.OrderId,
                    activity?.TraceId.ToString(),
                    activity?.SpanId.ToString());

                return false;

            default:
                result = ProcessingResults.InvalidPayload;
                activity?.SetTag("error.type", "invalid_payload");
                activity?.SetStatus(ActivityStatusCode.Error, orderLookup.ErrorMessage ?? "Invalid enrichment payload.");

                logger.LogError(
                    "Order enrichment returned invalid payload for order {OrderId} {TraceId} {SpanId}",
                    orderEvent.OrderId,
                    activity?.TraceId.ToString(),
                    activity?.SpanId.ToString());

                return false;
        }

        var order = orderLookup.Order!;

        if (order.OrderId != orderEvent.OrderId)
        {
            result = ProcessingResults.InvalidPayload;
            activity?.SetTag("error.type", "order_id_mismatch");
            activity?.SetStatus(ActivityStatusCode.Error, "Order response does not match consumed orderId.");

            logger.LogError(
                "Order enrichment returned mismatched orderId {ReturnedOrderId} for consumed order {OrderId} {TraceId} {SpanId}",
                order.OrderId,
                orderEvent.OrderId,
                activity?.TraceId.ToString(),
                activity?.SpanId.ToString());

            return false;
        }

        if (string.Equals(order.Status, "published", StringComparison.OrdinalIgnoreCase) && order.PublishedAtUtc is null)
        {
            result = ProcessingResults.InvalidPayload;
            activity?.SetTag("error.type", "published_at_missing");
            activity?.SetStatus(ActivityStatusCode.Error, "publishedAtUtc is required when status is published.");

            logger.LogError(
                "Order enrichment payload missing publishedAtUtc for order {OrderId} {TraceId} {SpanId}",
                orderEvent.OrderId,
                activity?.TraceId.ToString(),
                activity?.SpanId.ToString());

            return false;
        }

        logger.LogInformation(
            "Order enrichment succeeded for order {OrderId} with status {Status} {TraceId} {SpanId}",
            order.OrderId,
            order.Status,
            activity?.TraceId.ToString(),
            activity?.SpanId.ToString());

        return true;
    }
}