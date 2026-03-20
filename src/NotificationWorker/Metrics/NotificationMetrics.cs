using System.Diagnostics.Metrics;

namespace NotificationWorker.Metrics;

public interface INotificationMetrics
{
    void RecordResult(string result);

    void RecordPersistenceResult(string result, TimeSpan duration);

    void RecordConsumeFailure(string deduplicationKey);
}

public static class NotificationResults
{
    public const string Persisted = "persisted";
    public const string InvalidPayload = "invalid_payload";
    public const string PersistenceFailed = "persistence_failed";
    public const string ConsumeFailed = "consume_failed";
    public const string UnexpectedError = "unexpected_error";
}

public sealed class NotificationMetrics : INotificationMetrics
{
    public const string MeterName = "NotificationWorker.Metrics";

    private static readonly HashSet<string> AllowedResults =
    [
        NotificationResults.Persisted,
        NotificationResults.InvalidPayload,
        NotificationResults.PersistenceFailed,
        NotificationResults.ConsumeFailed,
        NotificationResults.UnexpectedError
    ];

    private static readonly HashSet<string> PersistenceResults =
    [
        NotificationResults.Persisted,
        NotificationResults.PersistenceFailed
    ];

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _notificationsPersistedCounter;
    private readonly Histogram<double> _notificationsPersistenceDurationHistogram;
    private readonly ObservableGauge<long> _kafkaConsumerLagGauge;
    private readonly object _consumeFailureLock = new();
    private string? _lastConsumeFailureKey;
    private long _lastConsumeFailureTick;

    public NotificationMetrics(KafkaLagSnapshot lagSnapshot)
    {
        LagSnapshot = lagSnapshot;
        _notificationsPersistedCounter = _meter.CreateCounter<long>("notifications.persisted.total", unit: "{message}");
        _notificationsPersistenceDurationHistogram = _meter.CreateHistogram<double>("notifications.persistence.duration", unit: "ms");
        _kafkaConsumerLagGauge = _meter.CreateObservableGauge<long>("kafka.consumer.lag", ObserveLag, unit: "{message}");
    }

    private KafkaLagSnapshot LagSnapshot { get; }

    public void RecordResult(string result)
    {
        EnsureAllowedResult(result);
        RecordCounter(result);
    }

    public void RecordPersistenceResult(string result, TimeSpan duration)
    {
        EnsureAllowedResult(result);

        if (!PersistenceResults.Contains(result))
        {
            throw new ArgumentOutOfRangeException(nameof(result), result, "Unsupported notification persistence result.");
        }

        KeyValuePair<string, object?>[] tags =
        [
            new("result", result)
        ];

        _notificationsPersistedCounter.Add(1, tags);
        _notificationsPersistenceDurationHistogram.Record(duration.TotalMilliseconds, tags);
    }

    public void RecordConsumeFailure(string deduplicationKey)
    {
        lock (_consumeFailureLock)
        {
            var nowTick = Environment.TickCount64;

            if (string.Equals(_lastConsumeFailureKey, deduplicationKey, StringComparison.Ordinal)
                && nowTick - _lastConsumeFailureTick <= 250)
            {
                return;
            }

            _lastConsumeFailureKey = deduplicationKey;
            _lastConsumeFailureTick = nowTick;
        }

        RecordCounter(NotificationResults.ConsumeFailed);
    }

    private void EnsureAllowedResult(string result)
    {
        if (!AllowedResults.Contains(result))
        {
            throw new ArgumentOutOfRangeException(nameof(result), result, "Unsupported notification metric result.");
        }
    }

    private void RecordCounter(string result)
    {
        KeyValuePair<string, object?>[] tags =
        [
            new("result", result)
        ];

        _notificationsPersistedCounter.Add(1, tags);
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