namespace NotificationWorker.Data;

public sealed class PersistedNotification
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public string Description { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset PublishedAtUtc { get; set; }

    public DateTimeOffset ProcessedAtUtc { get; set; }

    public DateTimeOffset PersistedAtUtc { get; set; }

    public string TraceId { get; set; } = string.Empty;
}