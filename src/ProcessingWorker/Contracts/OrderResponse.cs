namespace ProcessingWorker.Contracts;

public sealed record OrderResponse(
    Guid OrderId,
    string Description,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PublishedAtUtc);