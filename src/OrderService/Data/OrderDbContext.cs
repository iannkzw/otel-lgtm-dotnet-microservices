using Microsoft.EntityFrameworkCore;

namespace OrderService.Data;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var order = modelBuilder.Entity<Order>();

        order.ToTable("orders");
        order.HasKey(entity => entity.Id);

        order.Property(entity => entity.Id)
            .HasColumnName("id");

        order.Property(entity => entity.Description)
            .HasColumnName("description")
            .IsRequired();

        order.Property(entity => entity.Status)
            .HasColumnName("status")
            .IsRequired();

        order.Property(entity => entity.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        order.Property(entity => entity.PublishedAtUtc)
            .HasColumnName("published_at_utc");

        var outbox = modelBuilder.Entity<OutboxMessage>();

        outbox.ToTable("outbox_messages");
        outbox.HasKey(e => e.Id);
        outbox.Property(e => e.Id).HasColumnName("id");
        outbox.Property(e => e.OrderId).HasColumnName("order_id");
        outbox.Property(e => e.Payload).HasColumnName("payload").IsRequired();
        outbox.Property(e => e.AggregateType).HasColumnName("aggregate_type").IsRequired();
        outbox.Property(e => e.EventType).HasColumnName("event_type").IsRequired();
        outbox.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").IsRequired();
        outbox.Property(e => e.Traceparent).HasColumnName("traceparent");
        outbox.Property(e => e.Tracestate).HasColumnName("tracestate");
        outbox.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        outbox.HasIndex(e => e.IdempotencyKey).IsUnique();
    }
}