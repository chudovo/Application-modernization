using Npgsql;

namespace Shop.After;

/// <summary>
/// After: a saga. The order service drives a fixed sequence of steps and writes down, before and after each one,
/// where the order is. Going forward: reserve, capture, confirm. If the business says no: refund, release, reject.
/// Because the position is stored, a process that dies in the middle is continued by the next one.
/// </summary>
public class SagaOrderProcess(NpgsqlDataSource orders, InventoryService inventory, PaymentService payments, Faults faults) : IOrderProcess
{
    private const int ForwardAttempts = 5;

    public async Task<OrderOutcome> PlaceOrderAsync(Guid requestId, string customer, string card, IReadOnlyList<Line> lines)
    {
        var orderId = Guid.NewGuid();

        // The order, its lines and the saga's starting position: one local transaction in the order service.
        // The unique request id makes a repeated request find the first order.
        await using (var connection = await orders.OpenConnectionAsync())
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            var inserted = await transaction.ExecuteAsync(
                "INSERT INTO orders.orders (id, request_id, customer, card, total, status) VALUES ($1, $2, $3, $4, $5, 'pending') ON CONFLICT (request_id) DO NOTHING",
                orderId, requestId, customer, card, Catalog.Total(lines));
            if (inserted == 1)
            {
                foreach (var line in lines)
                    await transaction.ExecuteAsync("INSERT INTO orders.order_lines (order_id, sku, quantity) VALUES ($1, $2, $3)", orderId, line.Sku, line.Quantity);
                await transaction.ExecuteAsync("INSERT INTO orders.sagas (order_id, step) VALUES ($1, 'reserve')", orderId);
            }
            await transaction.CommitAsync();
        }
        orderId = await orders.ScalarAsync<Guid>("SELECT id FROM orders.orders WHERE request_id = $1", requestId);

        await AdvanceAsync(orderId);
        return await OutcomeAsync(orderId);
    }

    /// <summary>Continues every order that is not finished. Run at start-up and on a timer.</summary>
    public async Task SettleAsync()
    {
        while (true)
        {
            var unfinished = new List<Guid>();
            await using (var command = orders.CreateCommand("SELECT order_id FROM orders.sagas WHERE step <> 'done'"))
            await using (var reader = await command.ExecuteReaderAsync())
                while (await reader.ReadAsync()) unfinished.Add(reader.GetGuid(0));
            if (unfinished.Count == 0) return;
            foreach (var orderId in unfinished)
            {
                try { await AdvanceAsync(orderId); }
                catch (SimulatedCrash) { /* that worker died; the loop is the next one */ }
            }
        }
    }

    /// <summary>Runs steps from the stored position until the saga is done or a service does not answer.</summary>
    private async Task AdvanceAsync(Guid orderId)
    {
        while (true)
        {
            var step = await orders.ScalarAsync<string>("SELECT step FROM orders.sagas WHERE order_id = $1", orderId);
            try
            {
                switch (step)
                {
                    case "reserve":
                        await faults.CallAsync("inventory", "inventory.reserve", async () => await inventory.ReserveAsync(orderId, await LinesAsync(orderId)));
                        faults.CrashPoint("after-stock");
                        await MoveAsync(orderId, "capture");
                        break;

                    case "capture":
                        var (card, total) = await PaymentDetailsAsync(orderId);
                        await faults.CallAsync("payments", "payments.capture", () => payments.CaptureAsync(orderId, card, total));
                        faults.CrashPoint("after-payment");
                        await MoveAsync(orderId, "confirm");
                        break;

                    case "confirm":
                        await FinishAsync(orderId, "confirmed");
                        return;

                    // Compensation, in the reverse order of the steps it undoes. Each can be repeated.
                    case "refund":
                        await faults.CallAsync("payments", "payments.refund", () => payments.RefundAsync(orderId));
                        await MoveAsync(orderId, "release");
                        break;

                    case "release":
                        await faults.CallAsync("inventory", "inventory.release", () => inventory.ReleaseAsync(orderId));
                        await MoveAsync(orderId, "reject");
                        break;

                    case "reject":
                        await FinishAsync(orderId, "rejected");
                        return;

                    default:
                        return;
                }
            }
            catch (BusinessRuleException e)
            {
                // A definite no. Nothing after this step has happened; undo what came before it.
                await orders.ExecuteAsync("UPDATE orders.sagas SET step = 'refund', attempts = 0, reason = $2 WHERE order_id = $1", orderId, e.Message);
            }
            catch (ServiceUnavailableException e)
            {
                // No answer. The step may or may not have happened, so it is asked for again, later: every step can be
                // repeated safely. Going forward there is a limit, after which the order is given up and undone.
                // Undoing has no limit. It must succeed.
                var compensating = step is "refund" or "release";
                var attempts = (int)(await orders.ScalarAsync<int?>("UPDATE orders.sagas SET attempts = attempts + 1 WHERE order_id = $1 RETURNING attempts", orderId))!;
                if (!compensating && attempts >= ForwardAttempts)
                    await orders.ExecuteAsync("UPDATE orders.sagas SET step = 'refund', attempts = 0, reason = $2 WHERE order_id = $1", orderId, e.Message);
                return;   // leave it for the next round
            }
        }
    }

    private Task<int> MoveAsync(Guid orderId, string step) =>
        orders.ExecuteAsync("UPDATE orders.sagas SET step = $2, attempts = 0 WHERE order_id = $1", orderId, step);

    private async Task FinishAsync(Guid orderId, string status)
    {
        await using var connection = await orders.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await transaction.ExecuteAsync("UPDATE orders.orders SET status = $2, reason = (SELECT reason FROM orders.sagas WHERE order_id = $1) WHERE id = $1", orderId, status);
        await transaction.ExecuteAsync("UPDATE orders.sagas SET step = 'done' WHERE order_id = $1", orderId);
        await transaction.CommitAsync();
    }

    private async Task<List<Line>> LinesAsync(Guid orderId)
    {
        var lines = new List<Line>();
        await using var command = orders.CreateCommand("SELECT sku, quantity FROM orders.order_lines WHERE order_id = $1");
        command.Parameters.AddWithValue(orderId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) lines.Add(new Line(reader.GetString(0), reader.GetInt32(1)));
        return lines;
    }

    private async Task<(string Card, decimal Total)> PaymentDetailsAsync(Guid orderId)
    {
        await using var command = orders.CreateCommand("SELECT card, total FROM orders.orders WHERE id = $1");
        command.Parameters.AddWithValue(orderId);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetString(0), reader.GetDecimal(1));
    }

    private async Task<OrderOutcome> OutcomeAsync(Guid orderId)
    {
        await using var command = orders.CreateCommand("SELECT status, reason FROM orders.orders WHERE id = $1");
        command.Parameters.AddWithValue(orderId);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new OrderOutcome(orderId, Enum.Parse<OrderStatus>(reader.GetString(0), ignoreCase: true), reader.IsDBNull(1) ? null : reader.GetString(1));
    }
}
