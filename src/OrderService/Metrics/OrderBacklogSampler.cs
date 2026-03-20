using Microsoft.EntityFrameworkCore;
using OrderService.Data;

namespace OrderService.Metrics;

public sealed class OrderBacklogSampler(
    IServiceScopeFactory serviceScopeFactory,
    OrderBacklogSnapshot snapshot,
    IConfiguration configuration,
    ILogger<OrderBacklogSampler> logger) : BackgroundService
{
    private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(
        int.TryParse(configuration["OTEL_ORDER_BACKLOG_REFRESH_SECONDS"], out var configuredSeconds)
            ? Math.Max(configuredSeconds, 5)
            : 15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshSnapshotAsync(stoppingToken);

        using var timer = new PeriodicTimer(_refreshInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshSnapshotAsync(stoppingToken);
        }
    }

    private async Task RefreshSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

            var counts = await dbContext.Orders
                .AsNoTracking()
                .Where(order => order.Status == OrderStatuses.PendingPublish || order.Status == OrderStatuses.PublishFailed)
                .GroupBy(order => order.Status)
                .Select(group => new
                {
                    Status = group.Key,
                    Count = group.LongCount()
                })
                .ToListAsync(cancellationToken);

            long pendingPublishCount = 0;
            long publishFailedCount = 0;

            foreach (var entry in counts)
            {
                if (entry.Status == OrderStatuses.PendingPublish)
                {
                    pendingPublishCount = entry.Count;
                    continue;
                }

                if (entry.Status == OrderStatuses.PublishFailed)
                {
                    publishFailedCount = entry.Count;
                }
            }

            snapshot.Update(pendingPublishCount, publishFailedCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to refresh order backlog snapshot.");
        }
    }
}