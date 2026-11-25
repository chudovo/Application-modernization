using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Crm;

/// <summary>Before: the application's SQL as it has run against SQL Server.</summary>
public class SqlServerStore(DbDataSource db) : StoreBase(db), ICrmStore
{
    private const string Columns = "id, name, email, region, vip";

    public async Task<Customer?> FindByEmailAsync(string email) =>
        (await ListAsync($"SELECT {Columns} FROM customers WHERE email = @p0", MapCustomer, email)).SingleOrDefault();

    public async Task<IReadOnlyList<Customer>> FindByNameAsync(string name) =>
        await ListAsync($"SELECT {Columns} FROM customers WHERE name = @p0 ORDER BY id", MapCustomer, name);

    public async Task<IReadOnlyList<string>> SearchNamesAsync(string part) =>
        await ListAsync("SELECT name FROM customers WHERE name LIKE '%' + @p0 + '%' ORDER BY id", r => r.GetString(0), part);

    public async Task<int> RegisterAsync(string name, string email, string region)
    {
        try
        {
            return await ScalarAsync<int>("INSERT customers (name, email, region) OUTPUT INSERTED.id VALUES (@p0, @p1, @p2)", name, email, region);
        }
        catch (SqlException e) when (e.Number is 2627 or 2601)
        {
            throw new DuplicateEmailException(email);
        }
    }

    public async Task RegisterOrUpdateAsync(string name, string email, string region, DateTime now)
    {
        await using var connection = (SqlConnection)await Db.OpenConnectionAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        // Try the insert; if the address is taken, update instead. On SQL Server a failed statement
        // leaves the transaction usable.
        try
        {
            await using var insert = new SqlCommand("INSERT customers (name, email, region) VALUES (@p0, @p1, @p2)", connection, transaction);
            Add(insert, name, email, region);
            await insert.ExecuteNonQueryAsync();
        }
        catch (SqlException e) when (e.Number is 2627 or 2601)
        {
            await using var update = new SqlCommand("UPDATE customers SET name = @p0 WHERE email = @p1", connection, transaction);
            Add(update, name, email);
            await update.ExecuteNonQueryAsync();
        }

        await using var audit = new SqlCommand("INSERT audit (at, message) VALUES (@p0, @p1)", connection, transaction);
        Add(audit, now, "registered or updated " + email);
        await audit.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    public async Task<IReadOnlyList<string>> NamesInOrderAsync() =>
        await ListAsync("SELECT name FROM customers ORDER BY name", r => r.GetString(0));

    public async Task<IReadOnlyList<string>> CallListAsync() =>
        await ListAsync("SELECT name FROM customers ORDER BY last_order_at, id", r => r.GetString(0));

    public async Task<IReadOnlyList<string>> VipNamesAsync(int page, int pageSize) =>
        await ListAsync("SELECT name FROM customers WHERE vip = 1 ORDER BY id OFFSET @p0 ROWS FETCH NEXT @p1 ROWS ONLY", r => r.GetString(0), (page - 1) * pageSize, pageSize);

    public async Task<int> AverageItemsPerOrderAsync() =>
        await ScalarAsync<int>("SELECT AVG(items) FROM orders");

    public async Task<int> DaysSinceLastOrderAsync(int customerId, DateTime now) =>
        await ScalarAsync<int>("SELECT DATEDIFF(day, last_order_at, @p1) FROM customers WHERE id = @p0", customerId, now);

    public async Task<int> PlaceOrderAsync(int customerId, DateTime placedAt, int items, decimal total)
    {
        var id = await ScalarAsync<int>("INSERT orders (customer_id, placed_at, items, total) OUTPUT INSERTED.id VALUES (@p0, @p1, @p2, @p3)", customerId, placedAt, items, total);
        await ScalarAsync<int>("UPDATE customers SET last_order_at = @p1 WHERE id = @p0; SELECT @@ROWCOUNT", customerId, placedAt);
        return id;
    }

    public async Task<decimal> RevenueAsync(string region) =>
        await ScalarAsync<decimal>("SELECT ISNULL(SUM(o.total), 0) FROM orders o JOIN customers c ON c.id = o.customer_id WHERE c.region = @p0", region);

    public async Task<string> NoteAsync(int customerId) =>
        await ScalarAsync<string>("SELECT ISNULL(note, 'no note on file') FROM customers WHERE id = @p0", customerId);

    public async Task<IReadOnlyList<string>> AuditAsync() =>
        await ListAsync("SELECT message FROM audit ORDER BY id", r => r.GetString(0));
}
