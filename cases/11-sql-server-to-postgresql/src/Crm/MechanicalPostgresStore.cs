using System.Data.Common;
using Npgsql;

namespace Crm;

/// <summary>
/// The first attempt: every statement of <see cref="SqlServerStore"/> translated to PostgreSQL syntax and nothing more.
/// It compiles and every statement runs. Kept in the repository because the tests run against it too.
/// </summary>
public class MechanicalPostgresStore(DbDataSource db) : StoreBase(db), ICrmStore
{
    private const string Columns = "id, name, email, region, vip";

    public async Task<Customer?> FindByEmailAsync(string email) =>
        (await ListAsync($"SELECT {Columns} FROM customers WHERE email = @p0", MapCustomer, email)).SingleOrDefault();

    public async Task<IReadOnlyList<Customer>> FindByNameAsync(string name) =>
        await ListAsync($"SELECT {Columns} FROM customers WHERE name = @p0 ORDER BY id", MapCustomer, name);

    public async Task<IReadOnlyList<string>> SearchNamesAsync(string part) =>
        await ListAsync("SELECT name FROM customers WHERE name LIKE '%' || @p0 || '%' ORDER BY id", r => r.GetString(0), part);

    public async Task<int> RegisterAsync(string name, string email, string region)
    {
        try
        {
            return await ScalarAsync<int>("INSERT INTO customers (name, email, region) VALUES (@p0, @p1, @p2) RETURNING id", name, email, region);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new DuplicateEmailException(email);
        }
    }

    public async Task RegisterOrUpdateAsync(string name, string email, string region, DateTime now)
    {
        await using var connection = (NpgsqlConnection)await Db.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            await using var insert = new NpgsqlCommand("INSERT INTO customers (name, email, region) VALUES (@p0, @p1, @p2)", connection, transaction);
            Add(insert, name, email, region);
            await insert.ExecuteNonQueryAsync();
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await using var update = new NpgsqlCommand("UPDATE customers SET name = @p0 WHERE email = @p1", connection, transaction);
            Add(update, name, email);
            await update.ExecuteNonQueryAsync();
        }

        await using var audit = new NpgsqlCommand("INSERT INTO audit (at, message) VALUES (@p0, @p1)", connection, transaction);
        Add(audit, now, "registered or updated " + email);
        await audit.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    public async Task<IReadOnlyList<string>> NamesInOrderAsync() =>
        await ListAsync("SELECT name FROM customers ORDER BY name", r => r.GetString(0));

    public async Task<IReadOnlyList<string>> CallListAsync() =>
        await ListAsync("SELECT name FROM customers ORDER BY last_order_at, id", r => r.GetString(0));

    public async Task<IReadOnlyList<string>> VipNamesAsync(int page, int pageSize) =>
        await ListAsync("SELECT name FROM customers WHERE vip = true ORDER BY id OFFSET @p0 LIMIT @p1", r => r.GetString(0), (page - 1) * pageSize, pageSize);

    public async Task<int> AverageItemsPerOrderAsync() =>
        await ScalarAsync<int>("SELECT AVG(items) FROM orders");

    public async Task<int> DaysSinceLastOrderAsync(int customerId, DateTime now) =>
        await ScalarAsync<int>("SELECT EXTRACT(DAY FROM (@p1 - last_order_at)) FROM customers WHERE id = @p0", customerId, now);

    public async Task<int> PlaceOrderAsync(int customerId, DateTime placedAt, int items, decimal total)
    {
        var id = await ScalarAsync<int>("INSERT INTO orders (customer_id, placed_at, items, total) VALUES (@p0, @p1, @p2, @p3) RETURNING id", customerId, placedAt, items, total);
        await ScalarAsync<int>("WITH changed AS (UPDATE customers SET last_order_at = @p1 WHERE id = @p0 RETURNING 1) SELECT count(*) FROM changed", customerId, placedAt);
        return id;
    }

    public async Task<decimal> RevenueAsync(string region) =>
        await ScalarAsync<decimal>("SELECT COALESCE(SUM(o.total), 0) FROM orders o JOIN customers c ON c.id = o.customer_id WHERE c.region = @p0", region);

    public async Task<string> NoteAsync(int customerId) =>
        await ScalarAsync<string>("SELECT COALESCE(note, 'no note on file') FROM customers WHERE id = @p0", customerId);

    public async Task<IReadOnlyList<string>> AuditAsync() =>
        await ListAsync("SELECT message FROM audit ORDER BY id", r => r.GetString(0));
}
