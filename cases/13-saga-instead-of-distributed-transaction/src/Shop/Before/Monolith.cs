using Npgsql;

namespace Shop.Before;

/// <summary>
/// Before: stock, payment and order live in one database, and placing an order is one transaction.
/// Whatever goes wrong, at whatever point, the database undoes all of it.
/// </summary>
public class MonolithOrderProcess(NpgsqlDataSource db, Faults faults) : IOrderProcess
{
    public async Task<OrderOutcome> PlaceOrderAsync(Guid requestId, string customer, string card, IReadOnlyList<Line> lines)
    {
        if (await FindAsync(requestId) is { } known) return known;

        var orderId = Guid.NewGuid();
        var total = Catalog.Total(lines);
        try
        {
            await using var connection = await db.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            await transaction.ExecuteAsync("INSERT INTO monolith.orders (id, request_id, customer, total, status) VALUES ($1, $2, $3, $4, 'confirmed')", orderId, requestId, customer, total);
            foreach (var line in lines)
            {
                if (await transaction.ExecuteAsync("UPDATE monolith.stock SET on_hand = on_hand - $2 WHERE sku = $1 AND on_hand >= $2", line.Sku, line.Quantity) == 0)
                    throw new BusinessRuleException($"Not enough stock for {line.Sku}.");
                await transaction.ExecuteAsync("INSERT INTO monolith.order_lines (order_id, sku, quantity) VALUES ($1, $2, $3)", orderId, line.Sku, line.Quantity);
            }

            faults.CrashPoint("after-stock");

            if (card == "declined" || total > 1000) throw new BusinessRuleException("The card was declined.");
            await transaction.ExecuteAsync("INSERT INTO monolith.payments (order_id, amount) VALUES ($1, $2)", orderId, total);

            faults.CrashPoint("after-payment");

            await transaction.CommitAsync();
            return new OrderOutcome(orderId, OrderStatus.Confirmed, null);
        }
        catch (BusinessRuleException e)
        {
            // The transaction is gone, and with it the stock change and the payment. Only the refusal is recorded.
            await db.ExecuteAsync("INSERT INTO monolith.orders (id, request_id, customer, total, status, reason) VALUES ($1, $2, $3, $4, 'rejected', $5)", orderId, requestId, customer, total, e.Message);
            return new OrderOutcome(orderId, OrderStatus.Rejected, e.Message);
        }
    }

    public Task SettleAsync() => Task.CompletedTask;   // nothing is ever left unfinished

    private async Task<OrderOutcome?> FindAsync(Guid requestId)
    {
        await using var command = db.CreateCommand("SELECT id, status, reason FROM monolith.orders WHERE request_id = $1");
        command.Parameters.AddWithValue(requestId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new OrderOutcome(reader.GetGuid(0), Enum.Parse<OrderStatus>(reader.GetString(1), ignoreCase: true), reader.IsDBNull(2) ? null : reader.GetString(2))
            : null;
    }
}
