namespace OrderService.Contracts;

public sealed record OrderCreatedEvent(
    Guid OrderId,
    string Description,
    DateTimeOffset CreatedAtUtc);