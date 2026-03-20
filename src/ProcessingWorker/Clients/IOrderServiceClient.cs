using ProcessingWorker.Contracts;

namespace ProcessingWorker.Clients;

public interface IOrderServiceClient
{
    Task<OrderLookupResult> GetOrderAsync(Guid orderId, CancellationToken cancellationToken);
}