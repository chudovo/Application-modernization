using Npgsql;
using Payments.Releases;

namespace Payments.Tests;

/// <summary>The states the schema passes through. Each one is live in production for a while.</summary>
public enum Schema { Legacy = 0, Expanded = 1, Constrained = 3, Relaxed = 4, Contracted = 5 }

/// <summary>
/// A fresh PostgreSQL database with the legacy schema and data, which can be moved forward step by step.
/// The server comes from the PAYMENTS_PG environment variable.
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private static readonly string Server = Environment.GetEnvironmentVariable("PAYMENTS_PG")
        ?? "Host=localhost;Port=54320;Username=postgres;Password=local_dev_only";

    private readonly string _name = "payments_" + Guid.NewGuid().ToString("N");
    public NpgsqlDataSource Db { get; private set; } = null!;
    public Schema State { get; private set; } = Schema.Legacy;

    public static async Task<TestDatabase> CreateAsync(Schema state = Schema.Legacy)
    {
        var database = new TestDatabase();
        await using (var admin = NpgsqlDataSource.Create(Server))
            await admin.CreateCommand($"CREATE DATABASE {database._name}").ExecuteNonQueryAsync();

        database.Db = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(Server) { Database = database._name, MaxPoolSize = 40 }.ConnectionString);
        await database.Db.CreateCommand(Sql("legacy-schema.sql")).ExecuteNonQueryAsync();
        await database.Db.CreateCommand(Sql("legacy-data.sql")).ExecuteNonQueryAsync();
        await database.AdvanceToAsync(state);
        return database;
    }

    /// <summary>Applies the steps between the current state and the target, the backfill included.</summary>
    public async Task AdvanceToAsync(Schema target)
    {
        var migrator = new Migrator(Db);
        var lockTimeout = TimeSpan.FromSeconds(2);
        if (State < Schema.Expanded && target >= Schema.Expanded) await migrator.RunAsync(Sql("steps/1-expand.sql"), lockTimeout);
        if (State < Schema.Constrained && target >= Schema.Constrained)
        {
            await new Backfill(Db).RunAsync();
            await migrator.RunAsync(Sql("steps/3-constrain.sql"), lockTimeout);
        }
        if (State < Schema.Relaxed && target >= Schema.Relaxed) await migrator.RunAsync(Sql("steps/4-relax.sql"), lockTimeout);
        if (State < Schema.Contracted && target >= Schema.Contracted) await migrator.RunAsync(Sql("steps/5-contract.sql"), lockTimeout);
        State = target;
    }

    public IPaymentStore Release(int number) => number switch
    {
        1 => new Release1(Db),
        2 => new Release2(Db),
        3 => new Release3(Db),
        4 => new Release4(Db),
        _ => throw new ArgumentOutOfRangeException(nameof(number)),
    };

    public async Task<T> ScalarAsync<T>(string sql) => (T)(await Db.CreateCommand(sql).ExecuteScalarAsync())!;

    public static string Sql(string file)
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
        await Db.DisposeAsync();
        await using var admin = NpgsqlDataSource.Create(Server);
        await admin.CreateCommand($"DROP DATABASE IF EXISTS {_name} WITH (FORCE)").ExecuteNonQueryAsync();
    }
}
