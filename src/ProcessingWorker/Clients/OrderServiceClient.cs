using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ProcessingWorker.Contracts;

namespace ProcessingWorker.Clients;

public sealed class OrderServiceClient(HttpClient httpClient) : IOrderServiceClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<OrderLookupResult> GetOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync($"orders/{orderId}", cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new OrderLookupResult(OrderLookupStatus.NotFound, HttpStatusCode: (int)response.StatusCode);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new OrderLookupResult(
                    OrderLookupStatus.HttpError,
                    HttpStatusCode: (int)response.StatusCode,
                    ErrorMessage: $"OrderService returned HTTP {(int)response.StatusCode}.");
            }

            var order = await response.Content.ReadFromJsonAsync<OrderResponse>(SerializerOptions, cancellationToken);

            return order is null
                ? new OrderLookupResult(OrderLookupStatus.InvalidPayload, ErrorMessage: "OrderService returned an empty payload.")
                : new OrderLookupResult(OrderLookupStatus.Success, order);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new OrderLookupResult(OrderLookupStatus.Timeout, ErrorMessage: ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return new OrderLookupResult(OrderLookupStatus.NetworkError, ErrorMessage: ex.Message);
        }
        catch (JsonException ex)
        {
            return new OrderLookupResult(OrderLookupStatus.InvalidPayload, ErrorMessage: ex.Message);
        }
    }
}