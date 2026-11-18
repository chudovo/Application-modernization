using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Shop.Monolith;

public class Product
{
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
}

public class StockItem
{
    public required string Sku { get; set; }
    public int Quantity { get; set; }
}

public class Order
{
    public int Id { get; set; }
    public required string CustomerEmail { get; set; }
    public DateTime CreatedUtc { get; set; }
    public decimal Total { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
}

public class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public required string Sku { get; set; }
    public required string Name { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

/// <summary>One database for everything: catalog, stock and orders.</summary>
public class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockItem> Stock => Set<StockItem>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Product>().HasKey(p => p.Sku);
        model.Entity<StockItem>().HasKey(s => s.Sku);
        model.Entity<Order>().HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.OrderId);
    }

    public void Seed()
    {
        Products.AddRange(
            new Product { Sku = "CHAIR-01", Name = "Oak chair", Price = 89.50m, IsActive = true },
            new Product { Sku = "DESK-02", Name = "Standing desk", Price = 349.00m, IsActive = true },
            new Product { Sku = "LAMP-07", Name = "Desk lamp", Price = 39.99m, IsActive = true },
            new Product { Sku = "SHELF-03", Name = "Wall shelf", Price = 120.00m, IsActive = false });
        Stock.AddRange(
            new StockItem { Sku = "CHAIR-01", Quantity = 10 },
            new StockItem { Sku = "DESK-02", Quantity = 2 },
            new StockItem { Sku = "LAMP-07", Quantity = 0 },
            new StockItem { Sku = "SHELF-03", Quantity = 5 });
        SaveChanges();
    }
}

/// <summary>An in-memory SQLite database that lives as long as the application: a stand-in for the real one.</summary>
public sealed class Database : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    public string ConnectionString { get; }

    public Database(string name)
    {
        ConnectionString = $"Data Source=file:{name}-{Guid.NewGuid():N}?mode=memory&cache=shared";
        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();
    }

    public void Dispose() => _keepAlive.Dispose();
}
