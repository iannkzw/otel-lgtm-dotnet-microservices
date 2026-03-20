using Microsoft.EntityFrameworkCore;

namespace NotificationWorker.Data;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<PersistedNotification> NotificationResults => Set<PersistedNotification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var notification = modelBuilder.Entity<PersistedNotification>();

        notification.ToTable("notification_results");
        notification.HasKey(entity => entity.Id);

        notification.Property(entity => entity.Id)
            .HasColumnName("id");

        notification.Property(entity => entity.OrderId)
            .HasColumnName("order_id")
            .IsRequired();

        notification.Property(entity => entity.Description)
            .HasColumnName("description")
            .IsRequired();

        notification.Property(entity => entity.Status)
            .HasColumnName("status")
            .IsRequired();

        notification.Property(entity => entity.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        notification.Property(entity => entity.PublishedAtUtc)
            .HasColumnName("published_at_utc")
            .IsRequired();

        notification.Property(entity => entity.ProcessedAtUtc)
            .HasColumnName("processed_at_utc")
            .IsRequired();

        notification.Property(entity => entity.PersistedAtUtc)
            .HasColumnName("persisted_at_utc")
            .IsRequired();

        notification.Property(entity => entity.TraceId)
            .HasColumnName("trace_id")
            .IsRequired();

        notification.HasIndex(entity => entity.OrderId)
            .HasDatabaseName("ix_notification_results_order_id");

        notification.HasIndex(entity => entity.TraceId)
            .HasDatabaseName("ix_notification_results_trace_id");
    }
}