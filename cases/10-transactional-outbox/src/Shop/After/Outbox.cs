using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Shop.After;

/// <summary>
/// After: the order and the event are written in one database transaction. Either both exist or neither does.
/// Publishing is somebody else's job (<see cref="OutboxRelay"/>), so the broker being down no longer concerns this code.
/// </summary>
public class OutboxOrderService(NpgsqlDataSource db, Faults faults) : IOrderService
{
    public async Task<Guid> PlaceOrderAsync(string customer, decimal total)
    {
        if (string.IsNullOrWhiteSpace(customer) || total <= 0) throw new ArgumentException("An order needs a customer and a positive total.");

        var orderId = Guid.NewGuid();
        await using var connection = await db.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var order = new NpgsqlCommand("INSERT INTO orders.orders (id, customer, total) VALUES ($1, $2, $3)", connection, transaction))
        {
            order.Parameters.AddWithValue(orderId);
            order.Parameters.AddWithValue(customer);
            order.Parameters.AddWithValue(total);
            await order.ExecuteNonQueryAsync();
        }

        await using (var outbox = new NpgsqlCommand("INSERT INTO orders.outbox (id, type, payload) VALUES ($1, $2, $3)", connection, transaction))
        {
            outbox.Parameters.AddWithValue(Guid.NewGuid());
            outbox.Parameters.AddWithValue(nameof(OrderPlaced));
            outbox.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(new OrderPlaced(orderId, customer, total)));
            await outbox.ExecuteNonQueryAsync();
        }

        faults.Hit("before-commit");   // the process dies here: the transaction is rolled back, order and event together

        await transaction.CommitAsync();
        return orderId;
    }
}

/// <summary>
/// Publishes what is in the outbox. Runs as a background loop, in one or several instances. Delivery is at least once:
/// if the process dies between publishing and recording that it published, the message is published again.
/// </summary>
public class OutboxRelay(NpgsqlDataSource db, IPublisher publisher, Faults faults)
{
    /// <summary>Publishes one batch. Returns how many messages were published.</summary>
    public async Task<int> PublishPendingAsync(int batchSize = 20)
    {
        await using var connection = await db.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        // SKIP LOCKED: a second relay takes the next rows instead of waiting for these.
        var batch = new List<Message>();
        await using (var select = new NpgsqlCommand(
            "SELECT id, type, payload::text FROM orders.outbox WHERE published_at IS NULL ORDER BY created_at LIMIT $1 FOR UPDATE SKIP LOCKED", connection, transaction))
        {
            select.Parameters.AddWithValue(batchSize);
            await using var reader = await select.ExecuteReaderAsync();
            while (await reader.ReadAsync()) batch.Add(new Message(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        }

        var published = 0;
        try
        {
            foreach (var message in batch)
            {
                await publisher.PublishAsync(message);   // the outbox row's id travels as the message id

                faults.Hit("after-publish");   // the process dies here: published, but not recorded as published

                await using var mark = new NpgsqlCommand("UPDATE orders.outbox SET published_at = now() WHERE id = $1", connection, transaction);
                mark.Parameters.AddWithValue(message.Id);
                await mark.ExecuteNonQueryAsync();
                published++;
            }
        }
        catch (BrokerUnavailableException)
        {
            // Keep what was published so far. The rest stays in the outbox for the next round.
        }

        await transaction.CommitAsync();
        return published;
    }

    public async Task RunAsync(CancellationToken stop, TimeSpan? pause = null)
    {
        while (!stop.IsCancellationRequested)
        {
            int published;
            try { published = await PublishPendingAsync(); }
            catch (SimulatedCrash) { published = 0; }   // the next loop iteration is the restarted process
            if (published == 0) await Task.Delay(pause ?? TimeSpan.FromMilliseconds(50), CancellationToken.None);
        }
    }
}

/// <summary>
/// After: a shipment is created once per message, however often the message arrives. The record that the message was
/// handled and the shipment are written in one transaction.
/// </summary>
public class IdempotentShippingHandler(NpgsqlDataSource db)
{
    public async Task HandleAsync(Message message)
    {
        await using var connection = await db.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var seen = new NpgsqlCommand("INSERT INTO shipping.processed_messages (message_id) VALUES ($1) ON CONFLICT DO NOTHING", connection, transaction))
        {
            seen.Parameters.AddWithValue(message.Id);
            if (await seen.ExecuteNonQueryAsync() == 0) return;   // handled before: nothing to do, the caller acknowledges
        }

        var placed = message.Event;
        await using (var shipment = new NpgsqlCommand("INSERT INTO shipping.shipments (order_id, customer) VALUES ($1, $2)", connection, transaction))
        {
            shipment.Parameters.AddWithValue(placed.OrderId);
            shipment.Parameters.AddWithValue(placed.Customer);
            await shipment.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }
}
