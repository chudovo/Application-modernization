using Npgsql;

namespace Payments;

public record Payment(long Id, string Customer, decimal Amount);

/// <summary>
/// What the rest of the application sees. It does not change during the migration: amounts are decimals with two
/// places here, whatever the table stores. Each release of the application is one implementation.
/// </summary>
public interface IPaymentStore
{
    string Release { get; }
    Task<long> RecordAsync(string customer, decimal amount);
    Task<Payment?> GetAsync(long id);
    Task AdjustAsync(long id, decimal newAmount);
    Task<decimal> TotalForAsync(string customer);
    Task<IReadOnlyList<Payment>> RecentAsync(int count);
}

public abstract class StoreBase(NpgsqlDataSource db)
{
    protected NpgsqlDataSource Db => db;

    protected static long ToCents(decimal amount) => (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
    protected static decimal FromCents(long cents) => cents / 100m;

    /// <summary>How the old application has always shown a stored floating point amount.</summary>
    protected static decimal FromFloat(double amount) => (decimal)Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    protected async Task<T?> ScalarAsync<T>(string sql, params object[] parameters)
    {
        await using var command = Db.CreateCommand(sql);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter);
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    protected async Task<int> ExecuteAsync(string sql, params object[] parameters)
    {
        await using var command = Db.CreateCommand(sql);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter);
        return await command.ExecuteNonQueryAsync();
    }

    protected async Task<List<Payment>> QueryAsync(string sql, Func<NpgsqlDataReader, Payment> map, params object[] parameters)
    {
        await using var command = Db.CreateCommand(sql);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<Payment>();
        while (await reader.ReadAsync()) rows.Add(map(reader));
        return rows;
    }
}
