using System.Diagnostics.Metrics;

namespace ProcessingWorker.Metrics;

public interface IProcessingMetrics
{
    void RecordProcessingResult(string result, TimeSpan duration);
}

public static class ProcessingResults
{
    public const string Processed = "processed";
    public const string InvalidPayload = "invalid_payload";
    public const string NotFound = "not_found";
    public const string HttpError = "http_error";
    public const string Timeout = "timeout";
    public const string NetworkError = "network_error";
    public const string PublishFailed = "publish_failed";
    public const string UnexpectedError = "unexpected_error";
}

public sealed class ProcessingMetrics : IProcessingMetrics
{
    public const string MeterName = "ProcessingWorker.Metrics";

    private static readonly HashSet<string> AllowedResults =
    [
        ProcessingResults.Processed,
        ProcessingResults.InvalidPayload,
        ProcessingResults.NotFound,
        ProcessingResults.HttpError,
        ProcessingResults.Timeout,
        ProcessingResults.NetworkError,
        ProcessingResults.PublishFailed,
        ProcessingResults.UnexpectedError
    ];

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _ordersProcessedCounter;
    private readonly Histogram<double> _ordersProcessingDurationHistogram;
    private readonly ObservableGauge<long> _kafkaConsumerLagGauge;

    public ProcessingMetrics(KafkaLagSnapshot lagSnapshot)
    {
        LagSnapshot = lagSnapshot;
        _ordersProcessedCounter = _meter.CreateCounter<long>("orders.processed.total", unit: "{message}");
        _ordersProcessingDurationHistogram = _meter.CreateHistogram<double>("orders.processing.duration", unit: "ms");
        _kafkaConsumerLagGauge = _meter.CreateObservableGauge<long>("kafka.consumer.lag", ObserveLag, unit: "{message}");
    }

    private KafkaLagSnapshot LagSnapshot { get; }

    public void RecordProcessingResult(string result, TimeSpan duration)
    {
        if (!AllowedResults.Contains(result))
        {
            throw new ArgumentOutOfRangeException(nameof(result), result, "Unsupported processing metric result.");
        }

        KeyValuePair<string, object?>[] tags =
        [
            new("result", result)
        ];

        _ordersProcessedCounter.Add(1, tags);
        _ordersProcessingDurationHistogram.Record(duration.TotalMilliseconds, tags);
    }

    private IEnumerable<Measurement<long>> ObserveLag()
    {
        var snapshot = LagSnapshot.Read();

        return
        [
            new Measurement<long>(snapshot.Lag,
            [
                new KeyValuePair<string, object?>("topic", snapshot.Topic),
                new KeyValuePair<string, object?>("consumer_group", snapshot.ConsumerGroup)
            ])
        ];
    }
}