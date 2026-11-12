using Microsoft.EntityFrameworkCore;
using OrderDesk.Domain;

namespace OrderDesk.Data;

public class OrderDeskContext(DbContextOptions<OrderDeskContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().Property(c => c.Name).IsRequired().HasMaxLength(200);
        modelBuilder.Entity<OrderLine>().Property(l => l.Sku).IsRequired().HasMaxLength(50);
        modelBuilder.Entity<Order>().HasOne(o => o.Customer).WithMany().HasForeignKey(o => o.CustomerId);
        modelBuilder.Entity<Order>().HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.OrderId);

        // EF6 mapped decimal to decimal(18,2) by default. EF Core has no default and warns about truncation,
        // so the precision the existing database already uses is stated explicitly.
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()).Where(p => p.ClrType == typeof(decimal)))
        {
            property.SetPrecision(18);
            property.SetScale(2);
        }
    }
}
