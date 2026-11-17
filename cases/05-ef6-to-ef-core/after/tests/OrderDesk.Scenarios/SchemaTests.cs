using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using OrderDesk.Data;
using Xunit;

namespace OrderDesk.Scenarios;

/// <summary>The EF Core model against the database that Entity Framework 6 created.</summary>
public class SchemaTests : IDisposable
{
    private readonly TestDatabase _database = TestDatabase.Create();
    public void Dispose() => _database.Dispose();

    // Tables, columns with type and nullability, keys, foreign keys with their delete rule, and indexes, one per line.
    private const string DescribeDatabase = """
        SELECT 'column ' + t.name COLLATE DATABASE_DEFAULT + '.' + c.name COLLATE DATABASE_DEFAULT + ' ' +
               CASE WHEN ty.name COLLATE DATABASE_DEFAULT IN ('nvarchar', 'nchar') THEN ty.name COLLATE DATABASE_DEFAULT + '(' + IIF(c.max_length = -1, 'max', CAST(c.max_length / 2 AS varchar)) + ')'
                    WHEN ty.name COLLATE DATABASE_DEFAULT IN ('varchar', 'varbinary') THEN ty.name COLLATE DATABASE_DEFAULT + '(' + IIF(c.max_length = -1, 'max', CAST(c.max_length AS varchar)) + ')'
                    WHEN ty.name COLLATE DATABASE_DEFAULT = 'decimal' THEN 'decimal(' + CAST(c.precision AS varchar) + ',' + CAST(c.scale AS varchar) + ')'
                    WHEN ty.name COLLATE DATABASE_DEFAULT = 'timestamp' THEN 'rowversion'
                    ELSE ty.name COLLATE DATABASE_DEFAULT END +
               IIF(c.is_nullable = 1, ' null', ' not null') + IIF(c.is_identity = 1, ' identity', '')
        FROM sys.tables t JOIN sys.columns c ON c.object_id = t.object_id JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        UNION ALL
        SELECT 'key ' + t.name COLLATE DATABASE_DEFAULT + ' ' + k.name COLLATE DATABASE_DEFAULT FROM sys.key_constraints k JOIN sys.tables t ON t.object_id = k.parent_object_id
        UNION ALL
        SELECT 'foreign key ' + t.name COLLATE DATABASE_DEFAULT + ' ' + f.name COLLATE DATABASE_DEFAULT + ' -> ' + p.name COLLATE DATABASE_DEFAULT + ' on delete ' + LOWER(f.delete_referential_action_desc) COLLATE DATABASE_DEFAULT
        FROM sys.foreign_keys f JOIN sys.tables t ON t.object_id = f.parent_object_id JOIN sys.tables p ON p.object_id = f.referenced_object_id
        UNION ALL
        SELECT 'index ' + t.name COLLATE DATABASE_DEFAULT + ' ' + i.name COLLATE DATABASE_DEFAULT + IIF(i.is_unique = 1, ' unique', '')
        FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id WHERE i.is_primary_key = 0 AND i.type > 0
        """;

    private List<string> Database(Func<string, bool> include) =>
        _database.Rows(DescribeDatabase).Where(include).OrderBy(line => line, StringComparer.Ordinal).ToList();

    private static bool IsHistoryTable(string line) => line.Contains("__MigrationHistory") || line.Contains("__EFMigrationsHistory");

    [Fact]
    public void The_model_describes_exactly_the_tables_that_exist()
    {
        using var db = OrderDeskContext.Open(_database.ConnectionString);
        var model = db.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        var expected = new List<string>();
        foreach (var table in model.Tables)
        {
            foreach (var column in table.Columns)
            {
                var identity = column.PropertyMappings.Any(m => m.Property.ValueGenerated == ValueGenerated.OnAdd && m.Property.ClrType == typeof(int));
                expected.Add($"column {table.Name}.{column.Name} {column.StoreType}{(column.IsNullable ? " null" : " not null")}{(identity ? " identity" : "")}");
            }
            expected.Add($"key {table.Name} {table.PrimaryKey!.Name}");
            foreach (var foreignKey in table.ForeignKeyConstraints)
                expected.Add($"foreign key {table.Name} {foreignKey.Name} -> {foreignKey.PrincipalTable.Name} on delete {foreignKey.OnDeleteAction.ToString().ToLowerInvariant()}");
            foreach (var index in table.Indexes)
                expected.Add($"index {table.Name} {index.Name}{(index.IsUnique ? " unique" : "")}");
        }
        expected.Sort(StringComparer.Ordinal);

        Assert.Equal(expected, Database(line => !IsHistoryTable(line)));
    }

    [Fact]
    public void The_baseline_migration_changes_no_table_and_is_recorded()
    {
        var before = Database(line => !IsHistoryTable(line));
        using var db = OrderDeskContext.Open(_database.ConnectionString);

        db.Database.Migrate();

        Assert.Equal(before, Database(line => !IsHistoryTable(line)));
        Assert.Equal(["20261117093211_Baseline"], db.Database.GetAppliedMigrations());
        Assert.False(db.Database.HasPendingModelChanges());

        // The history of the Entity Framework 6 migrations is left alone.
        Assert.Equal(1, _database.Scalar<int>("SELECT COUNT(*) FROM dbo.__MigrationHistory"));
    }

    [Fact]
    public void Existing_data_survives_the_baseline_migration()
    {
        using var db = OrderDeskContext.Open(_database.ConnectionString);
        db.Database.Migrate();

        var order = new OrderStore(_database.ConnectionString).GetOrder(1);
        Assert.Equal(707.00m, order.Total);
        Assert.Equal(["priority", "wholesale"], order.Tags);
    }
}
