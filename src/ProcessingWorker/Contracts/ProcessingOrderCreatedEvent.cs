namespace ProcessingWorker.Contracts;

public sealed record ProcessingOrderCreatedEvent(
    Guid OrderId,
    string Description,
    DateTimeOffset CreatedAtUtc);