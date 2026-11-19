using Npgsql;

namespace Payments;

/// <summary>Runs a schema step so that it cannot take the application down while it waits for a lock.</summary>
public class Migrator(NpgsqlDataSource db)
{
    public int Attempts { get; private set; }

    /// <summary>
    /// ALTER TABLE needs an exclusive lock. If any transaction still uses the table, the ALTER waits, and every query
    /// that arrives after it waits behind it: the table is unusable until the first transaction ends. With a
    /// lock_timeout the ALTER gives up quickly instead, the queue dissolves, and the step is tried again.
    /// </summary>
    public async Task RunAsync(string sql, TimeSpan lockTimeout, int maxAttempts = 50)
    {
        for (Attempts = 1; ; Attempts++)
        {
            await using var connection = await db.OpenConnectionAsync();
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"SET lock_timeout = '{(int)lockTimeout.TotalMilliseconds}ms'; {sql}";
                await command.ExecuteNonQueryAsync();
                return;
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.LockNotAvailable && Attempts < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }
    }
}

/// <summary>Step 2: brings amount_cents in line with amount for every row, while the application keeps running.</summary>
public class Backfill(NpgsqlDataSource db)
{
    /// <summary>
    /// Small batches in separate transactions: each holds its row locks for milliseconds. Rows another transaction is
    /// changing right now are skipped and picked up by a later batch. Can be stopped and started again at any point.
    ///
    /// The condition is "differs", not "is empty". While Release 1 and Release 2 run side by side, Release 1 changes
    /// amounts in rows that Release 2 has already given a value in the new column. Those rows are not empty, they are
    /// wrong, and a backfill that only looks for NULL never touches them. Until reads move to the new column the old
    /// one is the truth, so recomputing from it is always correct.
    /// </summary>
    public async Task<long> RunAsync(int batchSize = 1000)
    {
        long total = 0;
        while (true)
        {
            await using var command = db.CreateCommand(
                """
                UPDATE payments SET amount_cents = round(amount * 100)::bigint
                WHERE id IN (SELECT id FROM payments
                             WHERE amount_cents IS DISTINCT FROM round(amount * 100)::bigint
                             ORDER BY id LIMIT $1 FOR UPDATE SKIP LOCKED)
                """);
            command.Parameters.AddWithValue(batchSize);
            var updated = await command.ExecuteNonQueryAsync();
            if (updated == 0 && await MismatchesAsync() == 0) return total;
            total += updated;
        }
    }

    /// <summary>Rows where the two columns disagree. Must be zero before reads move to the new column.</summary>
    public async Task<long> MismatchesAsync()
    {
        await using var command = db.CreateCommand(
            "SELECT count(*) FROM payments WHERE amount_cents IS DISTINCT FROM round(amount * 100)::bigint");
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
