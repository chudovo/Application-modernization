using Npgsql;

namespace Payments.Releases;

/// <summary>Before: reads and writes the floating point column. Knows nothing about amount_cents.</summary>
public class Release1(NpgsqlDataSource db) : StoreBase(db), IPaymentStore
{
    public string Release => "1";

    public async Task<long> RecordAsync(string customer, decimal amount) =>
        await ScalarAsync<long>("INSERT INTO payments (customer, amount) VALUES ($1, $2) RETURNING id", customer, (double)amount);

    public async Task<Payment?> GetAsync(long id) =>
        (await QueryAsync("SELECT id, customer, amount FROM payments WHERE id = $1", Map, id)).SingleOrDefault();

    public async Task AdjustAsync(long id, decimal newAmount) =>
        await ExecuteAsync("UPDATE payments SET amount = $1 WHERE id = $2", (double)newAmount, id);

    public async Task<decimal> TotalForAsync(string customer) =>
        FromFloat(await ScalarAsync<double>("SELECT coalesce(sum(amount), 0) FROM payments WHERE customer = $1", customer));

    public async Task<IReadOnlyList<Payment>> RecentAsync(int count) =>
        await QueryAsync("SELECT id, customer, amount FROM payments ORDER BY id DESC LIMIT $1", Map, count);

    private static Payment Map(NpgsqlDataReader row) => new(row.GetInt64(0), row.GetString(1), FromFloat(row.GetDouble(2)));
}
