using System.Diagnostics;
using System.Text.Json;
using Confluent.Kafka;
using OpenTelemetry.Trace;
using ProcessingWorker.Contracts;
using ProcessingWorker.Extensions;

namespace ProcessingWorker.Messaging;

public sealed class KafkaNotificationPublisher(
    IProducer<string, string> producer,
    ILogger<KafkaNotificationPublisher> logger,
    IConfiguration configuration) : INotificationPublisher
{
    private static readonly ActivitySource ActivitySource = new(OtelExtensions.ActivitySourceName);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _topic = configuration["KAFKA_TOPIC_NOTIFICATIONS"] ?? "notifications";

    public async Task PublishAsync(NotificationRequestedEvent notification, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("kafka publish notifications", ActivityKind.Producer);

        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", _topic);
        activity?.SetTag("messaging.operation", "publish");
        activity?.SetTag("messaging.kafka.message.key", notification.OrderId.ToString());
        activity?.SetTag("order.id", notification.OrderId.ToString());

        var message = new Message<string, string>
        {
            Key = notification.OrderId.ToString(),
            Value = JsonSerializer.Serialize(notification, SerializerOptions),
            Headers = new Headers()
        };

        KafkaTracingHelper.Inject(Activity.Current, message.Headers);

        try
        {
            await producer.ProduceAsync(_topic, message, cancellationToken);

            logger.LogInformation(
                "Published notification event {OrderId} to Kafka topic {Topic} {TraceId} {SpanId}",
                notification.OrderId,
                _topic,
                Activity.Current?.TraceId.ToString(),
                Activity.Current?.SpanId.ToString());
        }
        catch (ProduceException<string, string> ex)
        {
            activity?.AddException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Error.Reason);

            logger.LogError(
                ex,
                "Failed to publish notification event {OrderId} to Kafka topic {Topic} {TraceId} {SpanId}",
                notification.OrderId,
                _topic,
                Activity.Current?.TraceId.ToString(),
                Activity.Current?.SpanId.ToString());

            throw;
        }
    }
}