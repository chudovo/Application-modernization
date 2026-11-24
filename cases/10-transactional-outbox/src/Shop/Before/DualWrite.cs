using Npgsql;

namespace Shop.Before;

/// <summary>
/// Before: the order is saved, then the event is published. Two systems, two separate writes, nothing that ties them
/// together. This is what most code looks like the day a monolith gets its first message broker.
/// </summary>
public class DualWriteOrderService(NpgsqlDataSource db, IPublisher publisher, Faults faults) : IOrderService
{
    public async Task<Guid> PlaceOrderAsync(string customer, decimal total)
    {
        if (string.IsNullOrWhiteSpace(customer) || total <= 0) throw new ArgumentException("An order needs a customer and a positive total.");

        var orderId = Guid.NewGuid();
        await using (var command = db.CreateCommand("INSERT INTO orders.orders (id, customer, total) VALUES ($1, $2, $3)"))
        {
            command.Parameters.AddWithValue(orderId);
            command.Parameters.AddWithValue(customer);
            command.Parameters.AddWithValue(total);
            await command.ExecuteNonQueryAsync();
        }

        faults.Hit("after-commit");   // the process dies here: the order exists, nobody will ever be told

        await publisher.PublishAsync(Message.For(Guid.NewGuid(), new OrderPlaced(orderId, customer, total)));
        return orderId;
    }
}

/// <summary>Before: every message that arrives creates a shipment.</summary>
public class NaiveShippingHandler(NpgsqlDataSource db)
{
    public async Task HandleAsync(Message message)
    {
        var placed = message.Event;
        await using var command = db.CreateCommand("INSERT INTO shipping.shipments (order_id, customer) VALUES ($1, $2)");
        command.Parameters.AddWithValue(placed.OrderId);
        command.Parameters.AddWithValue(placed.Customer);
        await command.ExecuteNonQueryAsync();
    }
}
