using OrderService.Contracts;

namespace OrderService.Messaging;

public interface IKafkaOrderPublisher
{
    Task PublishAsync(OrderCreatedEvent orderEvent, CancellationToken cancellationToken);
}