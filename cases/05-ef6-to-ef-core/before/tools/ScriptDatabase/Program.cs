// Writes the SQL that Entity Framework 6 Code First Migrations runs to create the database for the model in
// before/src/OrderDesk.Data, including the row in __MigrationHistory. The result is committed as database/legacy-schema.sql
// and is the "existing database" both versions are tested against.
using System.Data.Entity.Migrations;
using System.Data.Entity.Migrations.Infrastructure;
using System.Data.SqlClient;
using OrderDesk.Data;

var connectionString = Environment.GetEnvironmentVariable("ORDERDESK_SQL")
    ?? "Server=localhost,14330;User Id=sa;Password=Local_dev_only_1;TrustServerCertificate=True";
var database = "OrderDeskScript_" + Guid.NewGuid().ToString("N")[..8];
var builder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = database };

using (var master = new SqlConnection(connectionString))
{
    master.Open();
    new SqlCommand($"CREATE DATABASE [{database}]", master).ExecuteNonQuery();
}
try
{
    var configuration = new Configuration { TargetDatabase = new System.Data.Entity.Infrastructure.DbConnectionInfo(builder.ConnectionString, "System.Data.SqlClient") };
    var script = new MigratorScriptingDecorator(new DbMigrator(configuration)).ScriptUpdate(sourceMigration: null, targetMigration: null);
    File.WriteAllText(args[0], script.ReplaceLineEndings("\n"));
    Console.WriteLine($"Wrote {args[0]} ({script.Length} characters)");
}
finally
{
    SqlConnection.ClearAllPools();
    using var master = new SqlConnection(connectionString);
    master.Open();
    new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]", master).ExecuteNonQuery();
}
