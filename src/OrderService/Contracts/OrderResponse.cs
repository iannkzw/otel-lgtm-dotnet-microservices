using OrderService.Data;

namespace OrderService.Contracts;

public sealed record OrderResponse(
    Guid OrderId,
    string Description,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PublishedAtUtc)
{
    public static OrderResponse FromOrder(Order order) => new(
        order.Id,
        order.Description,
        order.Status,
        order.CreatedAtUtc,
        order.PublishedAtUtc);
}