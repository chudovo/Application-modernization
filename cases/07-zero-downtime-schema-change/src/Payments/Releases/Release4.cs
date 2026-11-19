using Npgsql;

namespace Payments.Releases;

/// <summary>After: reads and writes only the integer column. The old column may be dropped once only this release runs.</summary>
public class Release4(NpgsqlDataSource db) : StoreBase(db), IPaymentStore
{
    public string Release => "4";

    public async Task<long> RecordAsync(string customer, decimal amount) =>
        await ScalarAsync<long>("INSERT INTO payments (customer, amount_cents) VALUES ($1, $2) RETURNING id", customer, ToCents(amount));

    public async Task<Payment?> GetAsync(long id) =>
        (await QueryAsync("SELECT id, customer, amount_cents FROM payments WHERE id = $1", Map, id)).SingleOrDefault();

    public async Task AdjustAsync(long id, decimal newAmount) =>
        await ExecuteAsync("UPDATE payments SET amount_cents = $1 WHERE id = $2", ToCents(newAmount), id);

    public async Task<decimal> TotalForAsync(string customer) =>
        FromCents((long)await ScalarAsync<decimal>("SELECT coalesce(sum(amount_cents), 0) FROM payments WHERE customer = $1", customer));

    public async Task<IReadOnlyList<Payment>> RecentAsync(int count) =>
        await QueryAsync("SELECT id, customer, amount_cents FROM payments ORDER BY id DESC LIMIT $1", Map, count);

    private static Payment Map(NpgsqlDataReader row) => new(row.GetInt64(0), row.GetString(1), FromCents(row.GetInt64(2)));
}
