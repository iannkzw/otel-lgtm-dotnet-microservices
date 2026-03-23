namespace OrderService.Data;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string Payload { get; set; } = string.Empty;
    public string AggregateType { get; set; } = "Order";
    public string EventType { get; set; } = "OrderCreated";
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? Traceparent { get; set; }
    public string? Tracestate { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
