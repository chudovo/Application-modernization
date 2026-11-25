using System.Data.Common;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace Crm.Tests;

public enum Engine { SqlServer, PostgresMechanical, PostgresMechanicalWithSequences, Postgres }

/// <summary>
/// A fresh SQL Server database with the legacy schema and data and, for the PostgreSQL engines, a fresh PostgreSQL
/// database that the data was copied into. The servers come from CRM_SQLSERVER and CRM_PG.
/// </summary>
public sealed class TestDatabases : IAsyncDisposable
{
    private static readonly string SqlServer = Environment.GetEnvironmentVariable("CRM_SQLSERVER") ?? "Server=localhost,14330;User Id=sa;Password=Local_dev_only_1;TrustServerCertificate=True";
    private static readonly string Postgres = Environment.GetEnvironmentVariable("CRM_PG") ?? "Host=localhost;Port=54320;Username=postgres;Password=local_dev_only";

    private readonly string _name = "crm_" + Guid.NewGuid().ToString("N");
    public DbDataSource Source { get; private set; } = null!;
    public DbDataSource? Target { get; private set; }
    public ICrmStore Store { get; private set; } = null!;
    public DataCopy? Copy { get; private set; }

    public static async Task<TestDatabases> CreateAsync(Engine engine)
    {
        var databases = new TestDatabases();

        await using (var master = SqlClientFactory.Instance.CreateDataSource(SqlServer))
            await master.CreateCommand($"CREATE DATABASE [{databases._name}]").ExecuteNonQueryAsync();
        databases.Source = SqlClientFactory.Instance.CreateDataSource(new SqlConnectionStringBuilder(SqlServer) { InitialCatalog = databases._name, Pooling = false }.ConnectionString);
        await databases.Source.CreateCommand(Sql("sqlserver/schema.sql")).ExecuteNonQueryAsync();
        await databases.Source.CreateCommand(Sql("sqlserver/data.sql")).ExecuteNonQueryAsync();

        if (engine == Engine.SqlServer)
        {
            databases.Store = new SqlServerStore(databases.Source);
            return databases;
        }

        await using (var admin = NpgsqlDataSource.Create(Postgres))
            await admin.CreateCommand($"CREATE DATABASE {databases._name}").ExecuteNonQueryAsync();
        var target = new NpgsqlConnectionStringBuilder(Postgres) { Database = databases._name, MaxPoolSize = 5 }.ConnectionString;
        var finished = engine == Engine.Postgres;

        // The schema is created over a connection of its own, as a migration tool would. The driver reads the list of
        // types when a data source first connects; one that connected before CREATE EXTENSION does not know citext.
        await using (var setup = NpgsqlDataSource.Create(target + ";Pooling=false"))
            await setup.CreateCommand(Sql(finished ? "postgres/schema.sql" : "postgres/schema.mechanical.sql")).ExecuteNonQueryAsync();
        databases.Target = NpgsqlDataSource.Create(target);
        databases.Copy = new DataCopy(databases.Source, databases.Target);
        await databases.Copy.RunAsync(resetIdentities: engine != Engine.PostgresMechanical);
        databases.Store = finished ? new PostgresStore(databases.Target) : new MechanicalPostgresStore(databases.Target);
        return databases;
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
        await Source.DisposeAsync();
        await using (var master = SqlClientFactory.Instance.CreateDataSource(SqlServer))
            await master.CreateCommand($"DROP DATABASE [{_name}]").ExecuteNonQueryAsync();
        if (Target is null) return;
        await Target.DisposeAsync();
        await using var admin = NpgsqlDataSource.Create(Postgres);
        await admin.CreateCommand($"DROP DATABASE IF EXISTS {_name} WITH (FORCE)").ExecuteNonQueryAsync();
    }
}
