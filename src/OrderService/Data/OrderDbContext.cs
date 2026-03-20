using Microsoft.EntityFrameworkCore;

namespace OrderService.Data;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

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
    }
}