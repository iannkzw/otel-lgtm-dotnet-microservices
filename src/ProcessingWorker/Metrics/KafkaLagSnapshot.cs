using System.Threading;

namespace ProcessingWorker.Metrics;

public sealed class KafkaLagSnapshot
{
    private long _lag;
    private long _lastUpdatedUnixTimeMilliseconds;

    public KafkaLagSnapshot(string topic, string consumerGroup)
    {
        Topic = topic;
        ConsumerGroup = consumerGroup;
    }

    public string Topic { get; }

    public string ConsumerGroup { get; }

    public void Update(long lag)
    {
        Interlocked.Exchange(ref _lag, Math.Max(lag, 0));
        Interlocked.Exchange(ref _lastUpdatedUnixTimeMilliseconds, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public KafkaLagState Read()
    {
        var lastUpdatedUnixTimeMilliseconds = Interlocked.Read(ref _lastUpdatedUnixTimeMilliseconds);

        return new KafkaLagState(
            Topic,
            ConsumerGroup,
            Interlocked.Read(ref _lag),
            lastUpdatedUnixTimeMilliseconds > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(lastUpdatedUnixTimeMilliseconds)
                : (DateTimeOffset?)null);
    }
}

public readonly record struct KafkaLagState(
    string Topic,
    string ConsumerGroup,
    long Lag,
    DateTimeOffset? LastUpdatedUtc);