using Npgsql;
using Xunit;

namespace Payments.Tests;

/// <summary>What goes wrong when a step is done the simple way or in the wrong order. Each test shows one failure.</summary>
public class PitfallTests
{
    private const string AddColumn = "ALTER TABLE payments ADD COLUMN note text";

    [Fact]
    public async Task A_schema_change_waiting_for_a_lock_blocks_every_query_behind_it()
    {
        await using var database = await TestDatabase.CreateAsync();

        // Someone, somewhere, has a transaction open that has read the table: a report, a forgotten session.
        await using var reporting = await database.Db.OpenConnectionAsync();
        await using var transaction = await reporting.BeginTransactionAsync();
        await new NpgsqlCommand("SELECT count(*) FROM payments", reporting, transaction).ExecuteScalarAsync();

        // The schema change is started without a lock timeout. It waits for that transaction.
        var migration = Task.Run(async () => await database.Db.CreateCommand(AddColumn).ExecuteNonQueryAsync());
        await Task.Delay(500);
        Assert.False(migration.IsCompleted);

        // And now an ordinary read from the application waits too, behind the schema change.
        await using var application = await database.Db.OpenConnectionAsync();
        var read = new NpgsqlCommand("SET statement_timeout = '1s'; SELECT count(*) FROM payments", application);
        var error = await Assert.ThrowsAsync<PostgresException>(() => read.ExecuteScalarAsync());
        Assert.Equal(PostgresErrorCodes.QueryCanceled, error.SqlState);

        await transaction.RollbackAsync();
        await migration;
    }

    [Fact]
    public async Task With_a_lock_timeout_the_schema_change_gives_way_and_succeeds_on_a_later_attempt()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var reporting = await database.Db.OpenConnectionAsync();
        await using var transaction = await reporting.BeginTransactionAsync();
        await new NpgsqlCommand("SELECT count(*) FROM payments", reporting, transaction).ExecuteScalarAsync();

        var migrator = new Migrator(database.Db);
        var migration = Task.Run(() => migrator.RunAsync(AddColumn, lockTimeout: TimeSpan.FromMilliseconds(100)));

        // While the migration keeps trying, the application reads within its one-second limit, every time.
        for (var i = 0; i < 5; i++)
        {
            await using var application = await database.Db.OpenConnectionAsync();
            await new NpgsqlCommand("SET statement_timeout = '1s'; SELECT count(*) FROM payments", application).ExecuteScalarAsync();
            await Task.Delay(200);
        }
        Assert.False(migration.IsCompleted);

        await transaction.RollbackAsync();   // the long transaction ends
        await migration;

        Assert.True(migrator.Attempts > 1);
        Assert.Equal(1L, await database.ScalarAsync<long>("SELECT count(*) FROM information_schema.columns WHERE table_name = 'payments' AND column_name = 'note'"));
    }

    [Fact]
    public async Task A_release_1_instance_still_running_after_the_backfill_leaves_rows_that_disagree()
    {
        await using var database = await TestDatabase.CreateAsync(Schema.Expanded);
        var backfill = new Backfill(database.Db);
        await backfill.RunAsync();
        Assert.Equal(0, await backfill.MismatchesAsync());

        // One old instance was not replaced yet. It changes an amount and knows nothing about the new column.
        await database.Release(1).AdjustAsync(1, 25.00m);
        await database.Release(1).RecordAsync("acme", 5.00m);

        // The verification query finds both rows. Release 3 would show 19.99 for the first one.
        Assert.Equal(2, await backfill.MismatchesAsync());
    }

    [Fact]
    public async Task A_backfill_that_only_fills_empty_rows_misses_rows_made_stale_while_two_releases_overlapped()
    {
        await using var database = await TestDatabase.CreateAsync(Schema.Expanded);

        // Releases 1 and 2 side by side: Release 2 records a payment, Release 1 then corrects its amount.
        var id = await database.Release(2).RecordAsync("acme", 10.00m);
        await database.Release(1).AdjustAsync(id, 12.00m);

        // The obvious backfill: fill what is empty.
        await database.Db.CreateCommand("UPDATE payments SET amount_cents = round(amount * 100)::bigint WHERE amount_cents IS NULL").ExecuteNonQueryAsync();

        // Nothing is empty any more, and the corrected payment still says 10.00 in the new column.
        var backfill = new Backfill(database.Db);
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM payments WHERE amount_cents IS NULL"));
        Assert.Equal(1, await backfill.MismatchesAsync());
        Assert.Equal(1000L, await database.ScalarAsync<long>($"SELECT amount_cents FROM payments WHERE id = {id}"));

        // The backfill of this case compares instead, and repairs it.
        Assert.Equal(1, await backfill.RunAsync());
        Assert.Equal(0, await backfill.MismatchesAsync());
    }

    [Fact]
    public async Task The_backfill_can_be_interrupted_and_run_again()
    {
        await using var database = await TestDatabase.CreateAsync(Schema.Expanded);
        await database.Db.CreateCommand("UPDATE payments SET amount_cents = round(amount * 100)::bigint WHERE id <= 3").ExecuteNonQueryAsync();

        var backfill = new Backfill(database.Db);

        Assert.Equal(5, await backfill.RunAsync(batchSize: 2));
        Assert.Equal(0, await backfill.RunAsync(batchSize: 2));
        Assert.Equal(0, await backfill.MismatchesAsync());
    }

    [Fact]
    public async Task Sums_of_amounts_that_were_never_whole_cents_change_by_a_cent()
    {
        // Three payments of 100 / 3. The old release adds the floating point numbers and rounds the sum: 100.00.
        // The new column holds 3333 cents three times: 99.99. Neither is a bug; they are different definitions.
        await using var legacy = await TestDatabase.CreateAsync(Schema.Legacy);
        await using var migrated = await TestDatabase.CreateAsync(Schema.Contracted);

        Assert.Equal(100.00m, await legacy.Release(1).TotalForAsync("thirds"));
        Assert.Equal(99.99m, await migrated.Release(4).TotalForAsync("thirds"));
    }
}
