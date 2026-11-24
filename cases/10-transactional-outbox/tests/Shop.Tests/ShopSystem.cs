using Npgsql;
using RabbitMQ.Client;
using Shop.After;
using Shop.Before;

namespace Shop.Tests;

public enum Design { DualWrite, Outbox }

/// <summary>
/// Both services wired together for one test: a fresh PostgreSQL database, a fresh RabbitMQ exchange and queue, the
/// order service of the chosen design and the matching shipping consumer. The servers come from SHOP_PG and SHOP_AMQP.
/// </summary>
public sealed class ShopSystem : IAsyncDisposable
{
    private static readonly string Postgres = Environment.GetEnvironmentVariable("SHOP_PG") ?? "Host=localhost;Port=54320;Username=postgres;Password=local_dev_only";
    private static readonly string Amqp = Environment.GetEnvironmentVariable("SHOP_AMQP") ?? "amqp://guest:guest@localhost:56720";

    private readonly string _name = "shop_" + Guid.NewGuid().ToString("N");
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _relays = [];
    private IConnection _broker = null!;

    public Design Design { get; private init; }
    public NpgsqlDataSource Db { get; private set; } = null!;
    public Faults Faults { get; } = new();
    public IOrderService Orders { get; private set; } = null!;
    public RabbitPublisher Publisher { get; private set; } = null!;
    public RabbitConsumer Consumer { get; private set; } = null!;

    public static async Task<ShopSystem> StartAsync(Design design, int relays = 1)
    {
        var system = new ShopSystem { Design = design };

        await using (var admin = NpgsqlDataSource.Create(Postgres))
            await admin.CreateCommand($"CREATE DATABASE {system._name}").ExecuteNonQueryAsync();
        system.Db = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(Postgres) { Database = system._name, MaxPoolSize = 15 }.ConnectionString);
        await system.Db.CreateCommand(Schema()).ExecuteNonQueryAsync();

        system._broker = await new ConnectionFactory { Uri = new Uri(Amqp) }.CreateConnectionAsync();
        await using (var setup = await system._broker.CreateChannelAsync())
        {
            await setup.ExchangeDeclareAsync(system._name, ExchangeType.Fanout, durable: true, autoDelete: false);
            await setup.QueueDeclareAsync(system._name, durable: true, exclusive: false, autoDelete: false);
            await setup.QueueBindAsync(system._name, system._name, routingKey: "");
        }

        system.Publisher = new RabbitPublisher(system._broker, system._name, system.Faults);
        if (design == Design.DualWrite)
        {
            system.Orders = new DualWriteOrderService(system.Db, system.Publisher, system.Faults);
            system.Consumer = new RabbitConsumer(system._broker, system._name, new NaiveShippingHandler(system.Db).HandleAsync, system.Faults);
        }
        else
        {
            system.Orders = new OutboxOrderService(system.Db, system.Faults);
            system.Consumer = new RabbitConsumer(system._broker, system._name, new IdempotentShippingHandler(system.Db).HandleAsync, system.Faults);
            for (var i = 0; i < relays; i++)
                system._relays.Add(Task.Run(() => new OutboxRelay(system.Db, system.Publisher, system.Faults).RunAsync(system._stop.Token)));
        }
        await system.Consumer.StartAsync();
        return system;
    }

    public async Task<long> CountAsync(string sql) => (long)(await Db.CreateCommand(sql).ExecuteScalarAsync())!;
    public Task<long> OrdersAsync() => CountAsync("SELECT count(*) FROM orders.orders");
    public Task<long> ShipmentsAsync() => CountAsync("SELECT count(*) FROM shipping.shipments");
    public Task<long> OrdersWithoutShipmentAsync() => CountAsync("SELECT count(*) FROM orders.orders o WHERE NOT EXISTS (SELECT 1 FROM shipping.shipments s WHERE s.order_id = o.id)");
    public Task<long> OrdersShippedMoreThanOnceAsync() => CountAsync("SELECT count(*) FROM (SELECT order_id FROM shipping.shipments GROUP BY order_id HAVING count(*) > 1) d");

    /// <summary>Waits until the condition holds, for at most ten seconds. Returns whether it did.</summary>
    public static async Task<bool> EventuallyAsync(Func<Task<bool>> condition, int seconds = 10)
    {
        for (var attempt = 0; attempt < seconds * 20; attempt++)
        {
            if (await condition()) return true;
            await Task.Delay(50);
        }
        return false;
    }

    /// <summary>Long enough for anything that is going to arrive to have arrived.</summary>
    public static Task SettleAsync() => Task.Delay(1500);

    private static string Schema()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "database", "schema.sql");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException("schema.sql");
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        await Task.WhenAll(_relays);
        await Consumer.DisposeAsync();
        await using (var cleanup = await _broker.CreateChannelAsync())
        {
            await cleanup.QueueDeleteAsync(_name);
            await cleanup.ExchangeDeleteAsync(_name);
        }
        await _broker.DisposeAsync();
        await Db.DisposeAsync();
        await using var admin = NpgsqlDataSource.Create(Postgres);
        await admin.CreateCommand($"DROP DATABASE IF EXISTS {_name} WITH (FORCE)").ExecuteNonQueryAsync();
    }
}
