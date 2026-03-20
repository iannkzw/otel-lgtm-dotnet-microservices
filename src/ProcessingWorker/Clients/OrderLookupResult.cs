using ProcessingWorker.Contracts;

namespace ProcessingWorker.Clients;

public sealed record OrderLookupResult(
    OrderLookupStatus Status,
    OrderResponse? Order = null,
    int? HttpStatusCode = null,
    string? ErrorMessage = null);

public enum OrderLookupStatus
{
    Success,
    NotFound,
    HttpError,
    Timeout,
    NetworkError,
    InvalidPayload
}