using Npgsql;

namespace Shop.Attempt;

/// <summary>
/// The first version after the split: the body of the old transaction, with the database statements replaced by calls
/// to the two new services, and a catch block that tries to undo what was done. It is what the code looks like the day
/// after the monolith is cut in three. Kept in the repository because the tests run against it too.
/// </summary>
public class NaiveDistributedOrderProcess(NpgsqlDataSource orders, NaiveInventory inventory, NaivePayments payments, Faults faults) : IOrderProcess
{
    public async Task<OrderOutcome> PlaceOrderAsync(Guid requestId, string customer, string card, IReadOnlyList<Line> lines)
    {
        // As in the monolith: a request seen before returns the order it created.
        if (await orders.ScalarAsync<Guid?>("SELECT id FROM orders.orders WHERE request_id = $1", requestId) is { } known)
        {
            var status = await orders.ScalarAsync<string>("SELECT status FROM orders.orders WHERE id = $1", known);
            return new OrderOutcome(known, Enum.Parse<OrderStatus>(status!, ignoreCase: true), null);
        }

        var orderId = Guid.NewGuid();
        var total = Catalog.Total(lines);
        await orders.ExecuteAsync("INSERT INTO orders.orders (id, request_id, customer, card, total, status) VALUES ($1, $2, $3, $4, $5, 'pending')", orderId, requestId, customer, card, total);
        foreach (var line in lines)
            await orders.ExecuteAsync("INSERT INTO orders.order_lines (order_id, sku, quantity) VALUES ($1, $2, $3)", orderId, line.Sku, line.Quantity);

        try
        {
            foreach (var line in lines)
                await faults.CallAsync("inventory", "inventory.reserve", () => inventory.ReserveAsync(orderId, line));

            faults.CrashPoint("after-stock");

            try
            {
                await faults.CallAsync("payments", "payments.capture", () => payments.CaptureAsync(orderId, card, total));
            }
            catch (ServiceUnavailableException)
            {
                // "Probably a hiccup": try once more.
                await faults.CallAsync("payments", "payments.capture", () => payments.CaptureAsync(orderId, card, total));
            }

            faults.CrashPoint("after-payment");

            await orders.ExecuteAsync("UPDATE orders.orders SET status = 'confirmed' WHERE id = $1", orderId);
            return new OrderOutcome(orderId, OrderStatus.Confirmed, null);
        }
        catch (Exception e) when (e is BusinessRuleException or ServiceUnavailableException)
        {
            // Undo by hand what a rollback used to undo.
            await faults.CallAsync("inventory", "inventory.release", () => inventory.ReleaseAsync(orderId));
            await orders.ExecuteAsync("UPDATE orders.orders SET status = 'rejected', reason = $2 WHERE id = $1", orderId, e.Message);
            return new OrderOutcome(orderId, OrderStatus.Rejected, e.Message);
        }
    }

    public Task SettleAsync() => Task.CompletedTask;   // there is nothing that knows what was left unfinished
}

public class NaiveInventory(NpgsqlDataSource db)
{
    public async Task ReserveAsync(Guid orderId, Line line)
    {
        if (await db.ExecuteAsync("UPDATE inventory.stock SET on_hand = on_hand - $2 WHERE sku = $1 AND on_hand >= $2", line.Sku, line.Quantity) == 0)
            throw new BusinessRuleException($"Not enough stock for {line.Sku}.");
        await db.ExecuteAsync("INSERT INTO inventory.reservations (order_id, sku, quantity, state) VALUES ($1, $2, $3, 'held')", orderId, line.Sku, line.Quantity);
    }

    public async Task ReleaseAsync(Guid orderId)
    {
        await db.ExecuteAsync("UPDATE inventory.stock s SET on_hand = s.on_hand + r.quantity FROM inventory.reservations r WHERE r.order_id = $1 AND r.state = 'held' AND s.sku = r.sku", orderId);
        await db.ExecuteAsync("UPDATE inventory.reservations SET state = 'released' WHERE order_id = $1", orderId);
    }
}

public class NaivePayments(NpgsqlDataSource db)
{
    public async Task CaptureAsync(Guid orderId, string card, decimal amount)
    {
        if (card == "declined" || amount > 1000) throw new BusinessRuleException("The card was declined.");
        await db.ExecuteAsync("INSERT INTO payments.payments (order_id, amount, state) VALUES ($1, $2, 'captured')", orderId, amount);
    }
}
