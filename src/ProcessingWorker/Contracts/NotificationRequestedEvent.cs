namespace ProcessingWorker.Contracts;

public sealed record NotificationRequestedEvent(
    Guid OrderId,
    string Description,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset PublishedAtUtc,
    DateTimeOffset ProcessedAtUtc);