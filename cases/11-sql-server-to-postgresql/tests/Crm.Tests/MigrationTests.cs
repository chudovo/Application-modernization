using Npgsql;
using Xunit;

namespace Crm.Tests;

/// <summary>The move of the data itself, and differences that are not part of the shared checks.</summary>
public class MigrationTests
{
    [Fact]
    public async Task Every_row_arrives_and_the_checksums_agree()
    {
        await using var databases = await TestDatabases.CreateAsync(Engine.Postgres);

        var tables = await databases.Copy!.VerifyAsync();

        Assert.Equal(["customers 6 = 6", "orders 5 = 5", "audit 0 = 0"], tables.Select(t => $"{t.Table} {t.SourceRows} = {t.TargetRows}"));
        Assert.All(tables, t => Assert.True(t.Same, $"{t.Table} differs"));
    }

    [Fact]
    public async Task The_verification_notices_a_single_changed_value()
    {
        await using var databases = await TestDatabases.CreateAsync(Engine.Postgres);
        await databases.Target!.CreateCommand("UPDATE orders SET total = total + 0.01 WHERE id = 3").ExecuteNonQueryAsync();

        var tables = await databases.Copy!.VerifyAsync();

        Assert.False(tables.Single(t => t.Table == "orders").Same);
        Assert.True(tables.Single(t => t.Table == "customers").Same);
    }

    [Fact]
    public async Task Without_resetting_the_identities_the_first_insert_collides_with_a_copied_row()
    {
        await using var databases = await TestDatabases.CreateAsync(Engine.PostgresMechanical);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            databases.Target!.CreateCommand("INSERT INTO audit (at, message) VALUES (now(), 'first'); INSERT INTO orders (customer_id, placed_at, items, total) VALUES (1, now(), 1, 1)").ExecuteNonQueryAsync());

        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("orders_pkey", error.ConstraintName);
    }

    [Fact]
    public async Task A_citext_column_compared_with_a_plain_parameter_is_still_case_sensitive()
    {
        await using var databases = await TestDatabases.CreateAsync(Engine.Postgres);

        async Task<long> CountAsync(string sql)
        {
            await using var command = databases.Target!.CreateCommand(sql);
            command.Parameters.Add(new NpgsqlParameter("p0", "ANNA@EXAMPLE.COM"));
            return (long)(await command.ExecuteScalarAsync())!;
        }

        // The parameter is text, so PostgreSQL compares text with text.
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM customers WHERE email = @p0"));
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM customers WHERE email = @p0::citext"));
    }

    [Fact]
    public async Task The_placeholder_for_a_missing_note_is_cut_off_on_sql_server_and_complete_on_postgresql()
    {
        await using var before = await TestDatabases.CreateAsync(Engine.SqlServer);
        await using var after = await TestDatabases.CreateAsync(Engine.Postgres);

        // ISNULL takes the type of its first argument, nvarchar(10). COALESCE does not cut anything off.
        Assert.Equal("no note on", await before.Store.NoteAsync(2));
        Assert.Equal("no note on file", await after.Store.NoteAsync(2));
        Assert.Equal("key acct", await after.Store.NoteAsync(1));
    }
}
