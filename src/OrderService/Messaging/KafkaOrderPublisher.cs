using System.Diagnostics;
using System.Text.Json;
using Confluent.Kafka;
using OpenTelemetry.Trace;
using OrderService.Contracts;

namespace OrderService.Messaging;

public sealed class KafkaOrderPublisher(
    IProducer<string, string> producer,
    ILogger<KafkaOrderPublisher> logger,
    IConfiguration configuration) : IKafkaOrderPublisher
{
    private readonly string _topic = configuration["KAFKA_TOPIC_ORDERS"] ?? "orders";

    public async Task PublishAsync(OrderCreatedEvent orderEvent, CancellationToken cancellationToken)
    {
        using var activity = ActivitySourceHolder.ActivitySource.StartActivity("kafka publish orders", ActivityKind.Producer);

        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", _topic);
        activity?.SetTag("messaging.operation", "publish");
        activity?.SetTag("messaging.kafka.message.key", orderEvent.OrderId.ToString());

        var message = new Message<string, string>
        {
            Key = orderEvent.OrderId.ToString(),
            Value = JsonSerializer.Serialize(orderEvent),
            Headers = new Headers()
        };

        KafkaTracingHelper.Inject(Activity.Current, message.Headers);

        try
        {
            await producer.ProduceAsync(_topic, message, cancellationToken);
            logger.LogInformation(
                "Published order event {OrderId} to Kafka topic {Topic} {TraceId} {SpanId}",
                orderEvent.OrderId,
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
                "Failed to publish order event {OrderId} to Kafka topic {Topic} {TraceId} {SpanId}",
                orderEvent.OrderId,
                _topic,
                Activity.Current?.TraceId.ToString(),
                Activity.Current?.SpanId.ToString());

            throw;
        }
    }

    internal static class ActivitySourceHolder
    {
        internal static readonly ActivitySource ActivitySource = new(Extensions.OtelExtensions.ActivitySourceName);
    }
}