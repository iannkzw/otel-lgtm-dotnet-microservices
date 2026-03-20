using System.Diagnostics.Metrics;
using OrderService.Data;

namespace OrderService.Metrics;

public interface IOrderMetrics
{
    void RecordCreateResult(string result, TimeSpan duration);
}

public static class OrderCreateResults
{
    public const string Created = "created";
    public const string ValidationFailed = "validation_failed";
    public const string PersistFailed = "persist_failed";
    public const string PublishFailed = "publish_failed";
    public const string StatusUpdateFailed = "status_update_failed";
}

public sealed class OrderMetrics : IOrderMetrics
{
    public const string MeterName = "OrderService.Metrics";

    private static readonly HashSet<string> AllowedResults =
    [
        OrderCreateResults.Created,
        OrderCreateResults.ValidationFailed,
        OrderCreateResults.PersistFailed,
        OrderCreateResults.PublishFailed,
        OrderCreateResults.StatusUpdateFailed
    ];

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _ordersCreatedCounter;
    private readonly Histogram<double> _ordersCreateDurationHistogram;
    private readonly ObservableGauge<long> _ordersBacklogGauge;

    public OrderMetrics(OrderBacklogSnapshot backlogSnapshot)
    {
        BacklogSnapshot = backlogSnapshot;
        _ordersCreatedCounter = _meter.CreateCounter<long>("orders.created.total", unit: "{order}");
        _ordersCreateDurationHistogram = _meter.CreateHistogram<double>("orders.create.duration", unit: "ms");
        _ordersBacklogGauge = _meter.CreateObservableGauge<long>("orders.backlog.current", ObserveBacklog, unit: "{order}");
    }

    private OrderBacklogSnapshot BacklogSnapshot { get; }

    public void RecordCreateResult(string result, TimeSpan duration)
    {
        if (!AllowedResults.Contains(result))
        {
            throw new ArgumentOutOfRangeException(nameof(result), result, "Unsupported order creation metric result.");
        }

        KeyValuePair<string, object?>[] tags =
        [
            new("result", result)
        ];

        _ordersCreatedCounter.Add(1, tags);
        _ordersCreateDurationHistogram.Record(duration.TotalMilliseconds, tags);
    }

    private IEnumerable<Measurement<long>> ObserveBacklog()
    {
        var snapshot = BacklogSnapshot.Read();

        return
        [
            new Measurement<long>(snapshot.PendingPublishCount,
            [
                new KeyValuePair<string, object?>("status", OrderStatuses.PendingPublish)
            ]),
            new Measurement<long>(snapshot.PublishFailedCount,
            [
                new KeyValuePair<string, object?>("status", OrderStatuses.PublishFailed)
            ])
        ];
    }
}