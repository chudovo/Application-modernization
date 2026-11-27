using Npgsql;

namespace Shop.After;

/// <summary>
/// The inventory service. Each operation is one local transaction and can be asked for twice without harm:
/// the reservation rows, keyed by order, are the memory of what was already done.
/// </summary>
public class InventoryService(NpgsqlDataSource db)
{
    /// <summary>Takes all lines of an order out of stock, or none of them.</summary>
    public async Task ReserveAsync(Guid orderId, IReadOnlyList<Line> lines)
    {
        await using var connection = await db.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        // Asked before? Then the answer is the same as the first time.
        if (await transaction.ScalarAsync<long>("SELECT count(*) FROM inventory.reservations WHERE order_id = $1", orderId) > 0) return;

        foreach (var line in lines.OrderBy(l => l.Sku))   // always the same order, so two orders cannot block each other
        {
            if (await transaction.ExecuteAsync("UPDATE inventory.stock SET on_hand = on_hand - $2 WHERE sku = $1 AND on_hand >= $2", line.Sku, line.Quantity) == 0)
                throw new BusinessRuleException($"Not enough stock for {line.Sku}.");   // rolls back the lines before it
            await transaction.ExecuteAsync("INSERT INTO inventory.reservations (order_id, sku, quantity, state) VALUES ($1, $2, $3, 'held')", orderId, line.Sku, line.Quantity);
        }
        await transaction.CommitAsync();
    }

    /// <summary>Puts back what the order holds. An order that holds nothing, or was released before, is left alone.</summary>
    public async Task ReleaseAsync(Guid orderId)
    {
        await using var connection = await db.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await transaction.ExecuteAsync(
            """
            WITH released AS (UPDATE inventory.reservations SET state = 'released' WHERE order_id = $1 AND state = 'held' RETURNING sku, quantity)
            UPDATE inventory.stock s SET on_hand = s.on_hand + r.quantity FROM released r WHERE s.sku = r.sku
            """, orderId);
        await transaction.CommitAsync();
    }
}

/// <summary>The payment service. One capture per order: the unique index on order_id makes a repeated request harmless.</summary>
public class PaymentService(NpgsqlDataSource db)
{
    public async Task CaptureAsync(Guid orderId, string card, decimal amount)
    {
        if (card == "declined" || amount > 1000) throw new BusinessRuleException("The card was declined.");
        await db.ExecuteAsync("INSERT INTO payments.payments (order_id, amount, state) VALUES ($1, $2, 'captured') ON CONFLICT (order_id) DO NOTHING", orderId, amount);
    }

    public async Task RefundAsync(Guid orderId) =>
        await db.ExecuteAsync("UPDATE payments.payments SET state = 'refunded' WHERE order_id = $1 AND state = 'captured'", orderId);
}
