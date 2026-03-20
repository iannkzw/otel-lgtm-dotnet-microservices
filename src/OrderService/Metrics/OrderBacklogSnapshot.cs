using System.Threading;

namespace OrderService.Metrics;

public sealed class OrderBacklogSnapshot
{
    private long _pendingPublishCount;
    private long _publishFailedCount;
    private long _lastUpdatedUnixTimeMilliseconds;

    public void Update(long pendingPublishCount, long publishFailedCount)
    {
        Interlocked.Exchange(ref _pendingPublishCount, pendingPublishCount);
        Interlocked.Exchange(ref _publishFailedCount, publishFailedCount);
        Interlocked.Exchange(ref _lastUpdatedUnixTimeMilliseconds, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public OrderBacklogState Read()
    {
        var lastUpdatedUnixTimeMilliseconds = Interlocked.Read(ref _lastUpdatedUnixTimeMilliseconds);

        return new OrderBacklogState(
            Interlocked.Read(ref _pendingPublishCount),
            Interlocked.Read(ref _publishFailedCount),
            lastUpdatedUnixTimeMilliseconds > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(lastUpdatedUnixTimeMilliseconds)
                : (DateTimeOffset?)null);
    }
}

public readonly record struct OrderBacklogState(
    long PendingPublishCount,
    long PublishFailedCount,
    DateTimeOffset? LastUpdatedUtc);