namespace OrderService.Data;

public sealed class Order
{
    public Guid Id { get; set; }

    public string Description { get; set; } = string.Empty;

    public string Status { get; set; } = OrderStatuses.PendingPublish;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? PublishedAtUtc { get; set; }
}