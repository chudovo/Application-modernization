using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OrderDesk.Data;

public class OrderDeskContext(DbContextOptions<OrderDeskContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<Tag> Tags => Set<Tag>();

    public static OrderDeskContext Open(string connectionString) =>
        new(new DbContextOptionsBuilder<OrderDeskContext>().UseSqlServer(connectionString).Options);

    /// <summary>
    /// A value with more than two decimals was cut off by Entity Framework 6 (19.999 became 19.99). EF Core rounds it
    /// (20.00). Money must not change during a library upgrade, so the old behaviour is kept here, in one place,
    /// until the business decides otherwise.
    /// </summary>
    private static readonly ValueConverter<decimal, decimal> AsEf6Stored =
        new(value => decimal.Round(value, 2, MidpointRounding.ToZero), value => value);

    /// <summary>
    /// Everything Entity Framework 6 decided by convention has to be written down here, because EF Core's conventions
    /// differ. The database is not changed: the model is described so that it matches the tables that exist.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Customer>(customer =>
        {
            customer.HasKey(c => c.Id).HasName("PK_dbo.Customers");
            customer.Property(c => c.Name).HasMaxLength(200);
            customer.Property(c => c.Email).HasMaxLength(200);

            // An Entity Framework 6 complex type. The column names (Address_City) happen to be EF Core's default too.
            customer.ComplexProperty(c => c.Address, address =>
            {
                address.Property(a => a.Street).HasMaxLength(200);
                address.Property(a => a.City).HasMaxLength(100);
                address.Property(a => a.PostalCode).HasMaxLength(20);
                address.Property(a => a.CountryCode).HasMaxLength(2);
            });
        });

        model.Entity<Order>(order =>
        {
            order.HasKey(o => o.Id).HasName("PK_dbo.Orders");

            // EF6 mapped DateTime to datetime. EF Core would assume datetime2.
            order.Property(o => o.CreatedUtc).HasColumnType("datetime");
            order.Property(o => o.Total).HasPrecision(18, 2).HasConversion(AsEf6Stored);
            order.Property(o => o.Note).HasMaxLength(500);
            order.Property(o => o.RowVersion).IsRowVersion();

            // The class has no foreign key property. EF6 named the column Customer_Id, EF Core would look for CustomerId.
            order.HasOne(o => o.Customer).WithMany(c => c.Orders)
                .HasForeignKey("Customer_Id").IsRequired()
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_dbo.Orders_dbo.Customers_Customer_Id");
            order.HasIndex("Customer_Id").HasDatabaseName("IX_Customer_Id");

            // The many-to-many join table. EF6 called it TagOrders with Tag_Id and Order_Id,
            // EF Core would look for OrderTag with OrdersId and TagsId.
            order.HasMany(o => o.Tags).WithMany(t => t.Orders).UsingEntity<Dictionary<string, object>>(
                "TagOrders",
                right => right.HasOne<Tag>().WithMany().HasForeignKey("Tag_Id")
                    .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_dbo.TagOrders_dbo.Tags_Tag_Id"),
                left => left.HasOne<Order>().WithMany().HasForeignKey("Order_Id")
                    .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_dbo.TagOrders_dbo.Orders_Order_Id"),
                join =>
                {
                    join.ToTable("TagOrders");
                    join.HasKey("Tag_Id", "Order_Id").HasName("PK_dbo.TagOrders");
                    join.HasIndex("Tag_Id").HasDatabaseName("IX_Tag_Id");
                    join.HasIndex("Order_Id").HasDatabaseName("IX_Order_Id");
                });
        });

        model.Entity<OrderLine>(line =>
        {
            line.HasKey(l => l.Id).HasName("PK_dbo.OrderLines");
            line.Property(l => l.Sku).HasMaxLength(50);
            line.Property(l => l.UnitPrice).HasPrecision(18, 2).HasConversion(AsEf6Stored);
            line.HasOne(l => l.Order).WithMany(o => o.Lines).HasForeignKey(l => l.OrderId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_dbo.OrderLines_dbo.Orders_OrderId");
            line.HasIndex(l => l.OrderId).HasDatabaseName("IX_OrderId");
        });

        model.Entity<Tag>(tag =>
        {
            tag.HasKey(t => t.Id).HasName("PK_dbo.Tags");
            tag.Property(t => t.Name).HasMaxLength(50);
            tag.HasIndex(t => t.Name).IsUnique().HasDatabaseName("IX_Name");
        });
    }
}

/// <summary>Used by the dotnet-ef tooling to create the context when adding a migration.</summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<OrderDeskContext>
{
    public OrderDeskContext CreateDbContext(string[] args) =>
        OrderDeskContext.Open(Environment.GetEnvironmentVariable("ORDERDESK_SQL") ?? "Server=localhost;Database=OrderDesk;Integrated Security=True");
}
