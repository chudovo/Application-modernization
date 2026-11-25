using System.Data.Common;
using System.Security.Cryptography;
using System.Text;

namespace Crm;

/// <summary>Copies the rows of every table from SQL Server to PostgreSQL and proves that they arrived.</summary>
public class DataCopy(DbDataSource source, DbDataSource target)
{
    private static readonly (string Table, string Columns)[] Tables =
    [
        ("customers", "id, name, email, region, vip, note, last_order_at"),
        ("orders", "id, customer_id, placed_at, items, total"),
        ("audit", "id, at, message"),
    ];

    /// <param name="resetIdentities">
    /// Rows are copied with their ids. PostgreSQL's identity sequences do not notice: they still start at 1, and the
    /// first insert after the migration collides with an existing row. This moves each sequence past the highest id.
    /// </param>
    public async Task<long> RunAsync(bool resetIdentities)
    {
        long copied = 0;
        foreach (var (table, columns) in Tables)
        {
            await using var read = source.CreateCommand($"SELECT {columns} FROM {table} ORDER BY id");
            await using var reader = await read.ExecuteReaderAsync();
            var placeholders = string.Join(", ", Enumerable.Range(0, reader.FieldCount).Select(i => "@p" + i));
            while (await reader.ReadAsync())
            {
                await using var write = target.CreateCommand($"INSERT INTO {table} ({columns}) VALUES ({placeholders})");
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var parameter = write.CreateParameter();
                    parameter.ParameterName = "p" + i;
                    parameter.Value = reader.GetValue(i);
                    write.Parameters.Add(parameter);
                }
                await write.ExecuteNonQueryAsync();
                copied++;
            }

            if (resetIdentities)
            {
                await using var reset = target.CreateCommand(
                    $"SELECT setval(pg_get_serial_sequence('{table}', 'id'), COALESCE((SELECT max(id) FROM {table}), 0) + 1, false)");
                await reset.ExecuteScalarAsync();
            }
        }
        return copied;
    }

    /// <summary>Row count and a checksum over every value of every row, for each table on both sides.</summary>
    public async Task<List<(string Table, long SourceRows, long TargetRows, bool Same)>> VerifyAsync()
    {
        var result = new List<(string, long, long, bool)>();
        foreach (var (table, columns) in Tables)
        {
            var (sourceRows, sourceHash) = await FingerprintAsync(source, table, columns);
            var (targetRows, targetHash) = await FingerprintAsync(target, table, columns);
            result.Add((table, sourceRows, targetRows, sourceHash == targetHash));
        }
        return result;
    }

    private static async Task<(long Rows, string Hash)> FingerprintAsync(DbDataSource db, string table, string columns)
    {
        await using var command = db.CreateCommand($"SELECT {columns} FROM {table} ORDER BY id");
        await using var reader = await command.ExecuteReaderAsync();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long rows = 0;
        while (await reader.ReadAsync())
        {
            rows++;
            for (var i = 0; i < reader.FieldCount; i++) hash.AppendData(Encoding.UTF8.GetBytes(Normalize(reader.GetValue(i)) + "\u001f"));
            hash.AppendData("\u001e"u8);
        }
        return (rows, Convert.ToHexString(hash.GetHashAndReset()));
    }

    // The same value must give the same text whichever driver returned it.
    private static string Normalize(object value) => value switch
    {
        DBNull => "\u0000",
        bool b => b ? "1" : "0",
        DateTime d => d.ToString("yyyy-MM-ddTHH:mm:ss.fff"),
        decimal m => m.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!,
    };
}
