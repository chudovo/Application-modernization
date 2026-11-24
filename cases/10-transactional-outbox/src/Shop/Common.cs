using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Shop;

public record OrderPlaced(Guid OrderId, string Customer, decimal Total);

/// <summary>A message as it travels: an identity that stays the same when it is sent again, and the event.</summary>
public record Message(Guid Id, string Type, string Payload)
{
    public static Message For(Guid id, OrderPlaced e) => new(id, nameof(OrderPlaced), JsonSerializer.Serialize(e));
    public OrderPlaced Event => JsonSerializer.Deserialize<OrderPlaced>(Payload)!;
}

public interface IOrderService
{
    /// <summary>Stores the order and makes sure the shipping service hears about it.</summary>
    Task<Guid> PlaceOrderAsync(string customer, decimal total);
}

public interface IPublisher
{
    Task PublishAsync(Message message);
}

public class BrokerUnavailableException() : Exception("The message broker did not accept the message.");

/// <summary>The process died at this point: nothing after it runs. Thrown only by <see cref="Faults"/>.</summary>
public class SimulatedCrash(string point) : Exception($"Simulated crash at '{point}'.");

/// <summary>
/// Named points in the code where a test can make things go wrong: the broker refuses a message, or the process dies.
/// In production nothing is registered and every call returns immediately.
/// </summary>
public class Faults
{
    private readonly Dictionary<string, Func<Exception?>> _points = [];
    public void At(string point, Func<Exception?> decide) => _points[point] = decide;
    public void Clear() => _points.Clear();

    public void Hit(string point)
    {
        if (_points.TryGetValue(point, out var decide) && decide() is { } failure) throw failure;
    }
}

/// <summary>Publishes to a RabbitMQ exchange and waits for the broker to confirm that it has the message.</summary>
public sealed class RabbitPublisher(IConnection connection, string exchange, Faults faults) : IPublisher
{
    public int Published;

    public async Task PublishAsync(Message message)
    {
        faults.Hit("publish");   // the broker is unreachable

        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));
        var properties = new BasicProperties { MessageId = message.Id.ToString(), Type = message.Type, Persistent = true };
        await channel.BasicPublishAsync(exchange, routingKey: "", mandatory: false, properties, System.Text.Encoding.UTF8.GetBytes(message.Payload));
        Interlocked.Increment(ref Published);
    }
}

/// <summary>Receives messages from a queue and hands them to a handler. A message is acknowledged after the handler returns.</summary>
public sealed class RabbitConsumer(IConnection connection, string queue, Func<Message, Task> handle, Faults faults) : IAsyncDisposable
{
    private IChannel? _channel;
    public int Deliveries;

    public async Task StartAsync()
    {
        _channel = await connection.CreateChannelAsync();
        await _channel.BasicQosAsync(0, prefetchCount: 10, global: false);
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            Interlocked.Increment(ref Deliveries);
            var message = new Message(Guid.Parse(delivery.BasicProperties.MessageId!), delivery.BasicProperties.Type!, System.Text.Encoding.UTF8.GetString(delivery.Body.Span));
            try
            {
                await handle(message);
                faults.Hit("before-ack");   // the consumer dies after doing the work and before saying so
                await _channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
            }
            catch (SimulatedCrash)
            {
                // Not acknowledged: the broker delivers the message again.
                await _channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true);
            }
        };
        await _channel.BasicConsumeAsync(queue, autoAck: false, consumer);
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
    }
}
