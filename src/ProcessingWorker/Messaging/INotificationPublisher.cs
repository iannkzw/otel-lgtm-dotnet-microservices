using ProcessingWorker.Contracts;

namespace ProcessingWorker.Messaging;

public interface INotificationPublisher
{
    Task PublishAsync(NotificationRequestedEvent notification, CancellationToken cancellationToken);
}