using Microsoft.Data.SqlClient;

namespace OrderDesk.Scenarios;

/// <summary>
/// A fresh copy of the existing database for one test: the schema exactly as Entity Framework 6 created it
/// (database/legacy-schema.sql) and the rows the old application wrote (database/legacy-data.sql).
/// Needs a SQL Server; the address comes from the ORDERDESK_SQL environment variable.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private static readonly string Server = Environment.GetEnvironmentVariable("ORDERDESK_SQL")
        ?? "Server=localhost,14330;User Id=sa;Password=Local_dev_only_1;TrustServerCertificate=True";

    private readonly string _name = "OrderDesk_" + Guid.NewGuid().ToString("N");

    public string ConnectionString { get; }

    private TestDatabase()
    {
        // Built by hand: the string is read by two SQL clients. The old one (System.Data.SqlClient, used by Entity
        // Framework 6) rejects the spelling "Trust Server Certificate" that the new client's builder produces.
        ConnectionString = $"{Server.TrimEnd(';')};Database={_name};Pooling=False";
    }

    public static TestDatabase Create()
    {
        var database = new TestDatabase();
        Execute(Server, $"CREATE DATABASE [{database._name}]");
        database.Execute(File.ReadAllText(Find("legacy-schema.sql")));
        database.Execute(File.ReadAllText(Find("legacy-data.sql")));
        return database;
    }

    public void Execute(string sql) => Execute(ConnectionString, sql);

    public T Scalar<T>(string sql)
    {
        using var connection = new SqlConnection(ConnectionString);
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        return (T)command.ExecuteScalar();
    }

    public List<string> Rows(string sql)
    {
        using var connection = new SqlConnection(ConnectionString);
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            var values = new object[reader.FieldCount];
            reader.GetValues(values);
            rows.Add(string.Join(" | ", values.Select(v => v is DBNull ? "NULL" : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture))));
        }
        return rows;
    }

    public void Dispose() => Execute(Server, $"DROP DATABASE [{_name}]");

    private static void Execute(string connectionString, string sql)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    private static string Find(string file)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "database", file);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(file);
    }
}
