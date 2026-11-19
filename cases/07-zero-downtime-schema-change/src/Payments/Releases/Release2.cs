using Npgsql;

namespace Payments.Releases;

/// <summary>Writes both columns, still reads the old one. Can run next to Release 1.</summary>
public class Release2(NpgsqlDataSource db) : StoreBase(db), IPaymentStore
{
    public string Release => "2";

    public async Task<long> RecordAsync(string customer, decimal amount) =>
        await ScalarAsync<long>("INSERT INTO payments (customer, amount, amount_cents) VALUES ($1, $2, $3) RETURNING id",
            customer, (double)amount, ToCents(amount));

    public async Task<Payment?> GetAsync(long id) =>
        (await QueryAsync("SELECT id, customer, amount FROM payments WHERE id = $1", Map, id)).SingleOrDefault();

    public async Task AdjustAsync(long id, decimal newAmount) =>
        await ExecuteAsync("UPDATE payments SET amount = $1, amount_cents = $2 WHERE id = $3", (double)newAmount, ToCents(newAmount), id);

    public async Task<decimal> TotalForAsync(string customer) =>
        FromFloat(await ScalarAsync<double>("SELECT coalesce(sum(amount), 0) FROM payments WHERE customer = $1", customer));

    public async Task<IReadOnlyList<Payment>> RecentAsync(int count) =>
        await QueryAsync("SELECT id, customer, amount FROM payments ORDER BY id DESC LIMIT $1", Map, count);

    private static Payment Map(NpgsqlDataReader row) => new(row.GetInt64(0), row.GetString(1), FromFloat(row.GetDouble(2)));
}
