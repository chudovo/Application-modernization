using Npgsql;

namespace Payments.Releases;

/// <summary>Reads the new column, still writes both, so that Release 2 next to it and a rollback to it keep working.</summary>
public class Release3(NpgsqlDataSource db) : StoreBase(db), IPaymentStore
{
    public string Release => "3";

    public async Task<long> RecordAsync(string customer, decimal amount) =>
        await ScalarAsync<long>("INSERT INTO payments (customer, amount, amount_cents) VALUES ($1, $2, $3) RETURNING id",
            customer, (double)amount, ToCents(amount));

    public async Task<Payment?> GetAsync(long id) =>
        (await QueryAsync("SELECT id, customer, amount_cents FROM payments WHERE id = $1", Map, id)).SingleOrDefault();

    public async Task AdjustAsync(long id, decimal newAmount) =>
        await ExecuteAsync("UPDATE payments SET amount = $1, amount_cents = $2 WHERE id = $3", (double)newAmount, ToCents(newAmount), id);

    public async Task<decimal> TotalForAsync(string customer) =>
        FromCents((long)await ScalarAsync<decimal>("SELECT coalesce(sum(amount_cents), 0) FROM payments WHERE customer = $1", customer));

    public async Task<IReadOnlyList<Payment>> RecentAsync(int count) =>
        await QueryAsync("SELECT id, customer, amount_cents FROM payments ORDER BY id DESC LIMIT $1", Map, count);

    private static Payment Map(NpgsqlDataReader row) => new(row.GetInt64(0), row.GetString(1), FromCents(row.GetInt64(2)));
}
