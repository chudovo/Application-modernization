using Npgsql;

namespace Shop;

public record Line(string Sku, int Quantity);

public enum OrderStatus { Pending, Confirmed, Rejected }

public record OrderOutcome(Guid OrderId, OrderStatus Status, string? Reason);

/// <summary>Placing an order: take the goods out of stock, take the money, confirm. All three or none.</summary>
public interface IOrderProcess
{
    /// <param name="requestId">Chosen by the client. The same request sent twice is one order.</param>
    Task<OrderOutcome> PlaceOrderAsync(Guid requestId, string customer, string card, IReadOnlyList<Line> lines);

    /// <summary>Finishes whatever was left unfinished, as a background worker would after a restart.</summary>
    Task SettleAsync();
}

public static class Catalog
{
    private static readonly Dictionary<string, decimal> Prices = new() { ["CHAIR"] = 89.50m, ["DESK"] = 349.00m, ["LAMP"] = 39.99m };
    public static decimal Total(IEnumerable<Line> lines) => lines.Sum(l => Prices[l.Sku] * l.Quantity);
}

/// <summary>The business said no: not enough stock, card declined. Trying again will not help.</summary>
public class BusinessRuleException(string reason) : Exception(reason);

/// <summary>The other service did not answer. It may not have received the request, or it may have carried it out.</summary>
public class ServiceUnavailableException(string service) : Exception($"{service} did not answer.");

/// <summary>The process died at this point: nothing after it runs. Thrown only by <see cref="Faults"/>.</summary>
public class SimulatedCrash(string point) : Exception($"Simulated crash at '{point}'.");

public enum Fault { None, Unavailable, AnsweredTooLate, Crash }

/// <summary>
/// Named points where a test can make things go wrong. "Unavailable": the call fails and nothing happened.
/// "AnsweredTooLate": the call did its work, and the caller is told it failed. "Crash": the calling process dies.
/// </summary>
public class Faults
{
    private readonly Dictionary<string, Func<Fault>> _points = [];
    public void At(string point, Func<Fault> decide) => _points[point] = decide;
    public void Clear() => _points.Clear();
    public Fault Ask(string point) => _points.TryGetValue(point, out var decide) ? decide() : Fault.None;

    public void CrashPoint(string point)
    {
        if (Ask(point) == Fault.Crash) throw new SimulatedCrash(point);
    }

    /// <summary>A call to another service, with whatever the test decided goes wrong on the way there or back.</summary>
    public async Task CallAsync(string service, string point, Func<Task> work)
    {
        var fault = Ask(point);
        if (fault == Fault.Unavailable) throw new ServiceUnavailableException(service);
        await work();
        if (fault == Fault.AnsweredTooLate) throw new ServiceUnavailableException(service);
    }
}

public static class Db
{
    public static async Task<int> ExecuteAsync(this NpgsqlDataSource db, string sql, params object?[] values)
    {
        await using var command = db.CreateCommand(sql);
        foreach (var value in values) command.Parameters.AddWithValue(value ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync();
    }

    public static async Task<T?> ScalarAsync<T>(this NpgsqlDataSource db, string sql, params object?[] values)
    {
        await using var command = db.CreateCommand(sql);
        foreach (var value in values) command.Parameters.AddWithValue(value ?? DBNull.Value);
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    public static async Task<int> ExecuteAsync(this NpgsqlTransaction transaction, string sql, params object?[] values)
    {
        await using var command = new NpgsqlCommand(sql, transaction.Connection, transaction);
        foreach (var value in values) command.Parameters.AddWithValue(value ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync();
    }

    public static async Task<T?> ScalarAsync<T>(this NpgsqlTransaction transaction, string sql, params object?[] values)
    {
        await using var command = new NpgsqlCommand(sql, transaction.Connection, transaction);
        foreach (var value in values) command.Parameters.AddWithValue(value ?? DBNull.Value);
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }
}
