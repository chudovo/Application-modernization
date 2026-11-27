using Npgsql;
using Shop.After;
using Shop.Attempt;
using Shop.Before;

namespace Shop.Tests;

public enum Design { Monolith, FirstAttempt, Saga }

/// <summary>Whether the three sets of books agree with each other. Every number except the first three should be zero.</summary>
public record Books(long Confirmed, long Rejected, long Unfinished, long UnitsMissingFromStock, long PaidButNotConfirmed, long ConfirmedButNotPaid, long ChargedTwice)
{
    public bool Consistent => Unfinished + UnitsMissingFromStock + PaidButNotConfirmed + ConfirmedButNotPaid + ChargedTwice == 0;
}

/// <summary>One design of the order process on a fresh PostgreSQL database. The server comes from SHOP_PG.</summary>
public sealed class ShopSystem : IAsyncDisposable
{
    private static readonly string Postgres = Environment.GetEnvironmentVariable("SHOP_PG") ?? "Host=localhost;Port=54320;Username=postgres;Password=local_dev_only";
    private readonly Dictionary<string, int> _initialStock = new() { ["CHAIR"] = 10, ["DESK"] = 2, ["LAMP"] = 1 };

    private readonly string _name = "shop_" + Guid.NewGuid().ToString("N");
    private readonly List<NpgsqlDataSource> _pools = [];
    private NpgsqlDataSource _admin = null!;

    public Design Design { get; private init; }
    public Faults Faults { get; } = new();
    public IOrderProcess Orders { get; private set; } = null!;

    public static async Task<ShopSystem> StartAsync(Design design)
    {
        var system = new ShopSystem { Design = design };
        await using (var server = NpgsqlDataSource.Create(Postgres))
            await server.CreateCommand($"CREATE DATABASE {system._name}").ExecuteNonQueryAsync();

        // Each service gets a connection pool of its own. No transaction ever uses two of them.
        NpgsqlDataSource Pool()
        {
            var pool = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(Postgres) { Database = system._name, MaxPoolSize = 10 }.ConnectionString);
            system._pools.Add(pool);
            return pool;
        }

        system._admin = Pool();
        if (design == Design.Monolith)
        {
            await system._admin.ExecuteAsync(Sql("monolith.sql"));
            system.Orders = new MonolithOrderProcess(Pool(), system.Faults);
            return system;
        }

        await system._admin.ExecuteAsync(Sql("services.sql"));
        if (design == Design.FirstAttempt)
        {
            system.Orders = new NaiveDistributedOrderProcess(Pool(), new NaiveInventory(Pool()), new NaivePayments(Pool()), system.Faults);
        }
        else
        {
            await system._admin.ExecuteAsync(Sql("saga.sql"));
            system.Orders = new SagaOrderProcess(Pool(), new InventoryService(Pool()), new PaymentService(Pool()), system.Faults);
        }
        return system;
    }

    public async Task<int> StockAsync(string sku) =>
        await _admin.ScalarAsync<int>($"SELECT on_hand FROM {(Design == Design.Monolith ? "monolith" : "inventory")}.stock WHERE sku = $1", sku);

    public async Task SetStockAsync(string sku, int onHand)
    {
        await _admin.ExecuteAsync($"UPDATE {(Design == Design.Monolith ? "monolith" : "inventory")}.stock SET on_hand = $2 WHERE sku = $1", sku, onHand);
        _initialStock[sku] = onHand;
    }

    /// <summary>Compares stock, payments and orders, which after the split are kept by three different services.</summary>
    public async Task<Books> BooksAsync()
    {
        var o = Design == Design.Monolith ? "monolith" : "orders";
        var paid = Design == Design.Monolith ? "SELECT order_id FROM monolith.payments" : "SELECT order_id FROM payments.payments WHERE state = 'captured'";

        long missing = 0;
        foreach (var (sku, initial) in _initialStock)
        {
            var sold = await _admin.ScalarAsync<long>($"SELECT COALESCE(SUM(l.quantity), 0) FROM {o}.order_lines l JOIN {o}.orders x ON x.id = l.order_id WHERE x.status = 'confirmed' AND l.sku = $1", sku);
            missing += Math.Abs(initial - await StockAsync(sku) - sold);
        }

        return new Books(
            Confirmed: await _admin.ScalarAsync<long>($"SELECT count(*) FROM {o}.orders WHERE status = 'confirmed'"),
            Rejected: await _admin.ScalarAsync<long>($"SELECT count(*) FROM {o}.orders WHERE status = 'rejected'"),
            Unfinished: await _admin.ScalarAsync<long>($"SELECT count(*) FROM {o}.orders WHERE status = 'pending'"),
            UnitsMissingFromStock: missing,
            PaidButNotConfirmed: await _admin.ScalarAsync<long>($"SELECT count(DISTINCT p.order_id) FROM ({paid}) p JOIN {o}.orders x ON x.id = p.order_id WHERE x.status <> 'confirmed'"),
            ConfirmedButNotPaid: await _admin.ScalarAsync<long>($"SELECT count(*) FROM {o}.orders x WHERE x.status = 'confirmed' AND NOT EXISTS (SELECT 1 FROM ({paid}) p WHERE p.order_id = x.id)"),
            ChargedTwice: await _admin.ScalarAsync<long>($"SELECT count(*) FROM (SELECT order_id FROM ({paid}) p GROUP BY order_id HAVING count(*) > 1) d"));
    }

    private static string Sql(string file)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "database", file);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException(file);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var pool in _pools) await pool.DisposeAsync();
        await using var server = NpgsqlDataSource.Create(Postgres);
        await server.CreateCommand($"DROP DATABASE IF EXISTS {_name} WITH (FORCE)").ExecuteNonQueryAsync();
    }
}
