using System.Data.Common;

namespace Crm;

public record Customer(int Id, string Name, string Email, string Region, bool Vip);

public class DuplicateEmailException(string email) : Exception($"A customer with the e-mail address {email} already exists.");

/// <summary>What the application asks of its database. One implementation per database engine.</summary>
public interface ICrmStore
{
    Task<Customer?> FindByEmailAsync(string email);
    Task<IReadOnlyList<Customer>> FindByNameAsync(string name);
    Task<IReadOnlyList<string>> SearchNamesAsync(string part);
    Task<int> RegisterAsync(string name, string email, string region);
    /// <summary>Registers the customer or, if the e-mail address is known, updates the name. Writes an audit line either way.</summary>
    Task RegisterOrUpdateAsync(string name, string email, string region, DateTime now);
    Task<IReadOnlyList<string>> NamesInOrderAsync();
    /// <summary>Customers to call: those who have never ordered first, then by oldest last order.</summary>
    Task<IReadOnlyList<string>> CallListAsync();
    Task<IReadOnlyList<string>> VipNamesAsync(int page, int pageSize);
    Task<int> AverageItemsPerOrderAsync();
    Task<int> DaysSinceLastOrderAsync(int customerId, DateTime now);
    Task<int> PlaceOrderAsync(int customerId, DateTime placedAt, int items, decimal total);
    Task<decimal> RevenueAsync(string region);
    Task<string> NoteAsync(int customerId);
    Task<IReadOnlyList<string>> AuditAsync();
}

/// <summary>The plumbing both implementations share. Only the SQL differs.</summary>
public abstract class StoreBase(DbDataSource db)
{
    protected DbDataSource Db => db;

    protected static void Add(DbCommand command, params object?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "p" + i;
            parameter.Value = values[i] ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }

    protected async Task<T> ScalarAsync<T>(string sql, params object?[] values)
    {
        await using var command = Db.CreateCommand(sql);
        Add(command, values);
        var result = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(result!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    protected async Task<List<T>> ListAsync<T>(string sql, Func<DbDataReader, T> map, params object?[] values)
    {
        await using var command = Db.CreateCommand(sql);
        Add(command, values);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync()) rows.Add(map(reader));
        return rows;
    }

    protected static Customer MapCustomer(DbDataReader row) =>
        new(row.GetInt32(0), row.GetString(1), row.GetString(2), row.GetString(3), row.GetBoolean(4));
}
