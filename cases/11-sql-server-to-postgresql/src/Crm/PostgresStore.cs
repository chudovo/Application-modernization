using System.Data.Common;
using Npgsql;

namespace Crm;

/// <summary>After: the statements that had to change are marked with what SQL Server did without being asked.</summary>
public class PostgresStore(DbDataSource db) : StoreBase(db), ICrmStore
{
    private const string Columns = "id, name, email, region, vip";

    // SQL Server: every string comparison ignored letter case. Here the email column is citext (see schema.sql).
    // The cast matters: a parameter arrives as text, and citext = text is decided as text = text, case-sensitive.
    public async Task<Customer?> FindByEmailAsync(string email) =>
        (await ListAsync($"SELECT {Columns} FROM customers WHERE email = @p0::citext", MapCustomer, email)).SingleOrDefault();

    // SQL Server: '=' ignored letter case and trailing spaces.
    public async Task<IReadOnlyList<Customer>> FindByNameAsync(string name) =>
        await ListAsync($"SELECT {Columns} FROM customers WHERE lower(name) = lower(rtrim(@p0)) ORDER BY id", MapCustomer, name);

    // SQL Server: LIKE ignored letter case.
    public async Task<IReadOnlyList<string>> SearchNamesAsync(string part) =>
        await ListAsync("SELECT name FROM customers WHERE name ILIKE '%' || @p0 || '%' ORDER BY id", r => r.GetString(0), part);

    public async Task<int> RegisterAsync(string name, string email, string region)
    {
        try
        {
            return await ScalarAsync<int>("INSERT INTO customers (name, email, region) VALUES (@p0, @p1, @p2) RETURNING id", name, email, region);
        }
        // Only the constraint on the address means "duplicate address". Any other unique violation is a different problem.
        catch (PostgresException e) when (e is { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "uq_customers_email" })
        {
            throw new DuplicateEmailException(email);
        }
    }

    public async Task RegisterOrUpdateAsync(string name, string email, string region, DateTime now)
    {
        await using var connection = (NpgsqlConnection)await Db.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        // SQL Server: a failed INSERT left the transaction usable, so "try, catch, update" worked. In PostgreSQL the
        // first error ends the transaction. One statement that cannot fail on a duplicate replaces the pattern.
        await using (var upsert = new NpgsqlCommand(
            "INSERT INTO customers (name, email, region) VALUES (@p0, @p1, @p2) ON CONFLICT (email) DO UPDATE SET name = EXCLUDED.name", connection, transaction))
        {
            Add(upsert, name, email, region);
            await upsert.ExecuteNonQueryAsync();
        }

        await using var audit = new NpgsqlCommand("INSERT INTO audit (at, message) VALUES (@p0, @p1)", connection, transaction);
        Add(audit, now, "registered or updated " + email);
        await audit.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    // The order depends on the collation of the database. The ICU collation is named so that it does not.
    public async Task<IReadOnlyList<string>> NamesInOrderAsync() =>
        await ListAsync("SELECT name FROM customers ORDER BY name COLLATE \"und-x-icu\"", r => r.GetString(0));

    // SQL Server: NULL sorts first. PostgreSQL: last, unless told otherwise.
    public async Task<IReadOnlyList<string>> CallListAsync() =>
        await ListAsync("SELECT name FROM customers ORDER BY last_order_at NULLS FIRST, id", r => r.GetString(0));

    public async Task<IReadOnlyList<string>> VipNamesAsync(int page, int pageSize) =>
        await ListAsync("SELECT name FROM customers WHERE vip ORDER BY id OFFSET @p0 LIMIT @p1", r => r.GetString(0), (page - 1) * pageSize, pageSize);

    // SQL Server: AVG of an integer column is an integer, cut off. PostgreSQL returns 2.6. The dashboard shows what
    // it always showed until someone decides it should not.
    public async Task<int> AverageItemsPerOrderAsync() =>
        await ScalarAsync<int>("SELECT COALESCE(SUM(items) / NULLIF(COUNT(*), 0), 0) FROM orders");

    // SQL Server: DATEDIFF(day, a, b) counts midnights between a and b. Subtracting dates does the same;
    // EXTRACT(DAY FROM b - a) counts whole 24-hour periods, which is something else.
    public async Task<int> DaysSinceLastOrderAsync(int customerId, DateTime now) =>
        await ScalarAsync<int>("SELECT @p1::date - last_order_at::date FROM customers WHERE id = @p0", customerId, now);

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
