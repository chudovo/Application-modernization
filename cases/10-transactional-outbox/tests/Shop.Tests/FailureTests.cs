using Xunit;
using Xunit.Abstractions;

namespace Shop.Tests;

/// <summary>What happens when something fails between the database and the broker: first before, then after.</summary>
public class With_dual_write
{
    [Fact]
    public async Task Broker_unavailable_the_caller_gets_an_error_the_order_is_stored_and_never_shipped()
    {
        await using var shop = await ShopSystem.StartAsync(Design.DualWrite);
        shop.Faults.At("publish", () => new BrokerUnavailableException());

        await Assert.ThrowsAsync<BrokerUnavailableException>(() => shop.Orders.PlaceOrderAsync("anna@example.com", 100m));

        shop.Faults.Clear();   // the broker is back
        await ShopSystem.SettleAsync();
        Assert.Equal(1, await shop.OrdersAsync());      // the customer was told it failed, and it did not
        Assert.Equal(0, await shop.ShipmentsAsync());   // and nothing will ever ship it
    }

    [Fact]
    public async Task A_customer_who_retries_after_that_error_has_two_orders()
    {
        await using var shop = await ShopSystem.StartAsync(Design.DualWrite);
        var failures = 1;
        shop.Faults.At("publish", () => failures-- > 0 ? new BrokerUnavailableException() : null);

        await Assert.ThrowsAsync<BrokerUnavailableException>(() => shop.Orders.PlaceOrderAsync("anna@example.com", 100m));
        await shop.Orders.PlaceOrderAsync("anna@example.com", 100m);   // "it said error, so I clicked again"

        Assert.True(await ShopSystem.EventuallyAsync(async () => await shop.ShipmentsAsync() == 1));
        Assert.Equal(2, await shop.OrdersAsync());
    }

    [Fact]
    public async Task Process_dies_after_saving_the_order_and_the_event_is_lost()
    {
        await using var shop = await ShopSystem.StartAsync(Design.DualWrite);
        shop.Faults.At("after-commit", () => new SimulatedCrash("after-commit"));

        await Assert.ThrowsAsync<SimulatedCrash>(() => shop.Orders.PlaceOrderAsync("anna@example.com", 100m));

        await ShopSystem.SettleAsync();
        Assert.Equal(1, await shop.OrdersAsync());
        Assert.Equal(0, await shop.ShipmentsAsync());
    }

    [Fact]
    public async Task A_message_delivered_twice_is_shipped_twice()
    {
        await using var shop = await ShopSystem.StartAsync(Design.DualWrite);
        var crashes = 1;
        shop.Faults.At("before-ack", () => crashes-- > 0 ? new SimulatedCrash("before-ack") : null);

        await shop.Orders.PlaceOrderAsync("anna@example.com", 100m);

        // The consumer created the shipment, died before acknowledging, and the broker delivered the message again.
        Assert.True(await ShopSystem.EventuallyAsync(async () => await shop.ShipmentsAsync() == 2));
        Assert.Equal(1, await shop.OrdersShippedMoreThanOnceAsync());
    }
}

public class With_outbox(ITestOutputHelper output)
{
    [Fact]
    public async Task Broker_unavailable_the_order_is_accepted_and_shipped_when_the_broker_returns()
    {
        await using var shop = await ShopSystem.StartAsync(Design.Outbox);
        shop.Faults.At("publish", () => new BrokerUnavailableException());

        await shop.Orders.PlaceOrderAsync("anna@example.com", 100m);   // no error: the order service does not talk to the broker

        await ShopSystem.SettleAsync();
        Assert.Equal(0, await shop.ShipmentsAsync());
        Assert.Equal(1, await shop.CountAsync("SELECT count(*) FROM orders.outbox WHERE published_at IS NULL"));

        shop.Faults.Clear();   // the broker is back
        Assert.True(await ShopSystem.EventuallyAsync(async () => await shop.ShipmentsAsync() == 1));
        Assert.Equal(0, await shop.CountAsync("SELECT count(*) FROM orders.outbox WHERE published_at IS NULL"));
    }

    [Fact]
    public async Task Process_dies_before_the_commit_and_neither_order_nor_event_exists()
    {
        await using var shop = await ShopSystem.StartAsync(Design.Outbox);
        shop.Faults.At("before-commit", () => new SimulatedCrash("before-commit"));

        await Assert.ThrowsAsync<SimulatedCrash>(() => shop.Orders.PlaceOrderAsync("anna@example.com", 100m));

        await ShopSystem.SettleAsync();
        Assert.Equal(0, await shop.OrdersAsync());
        Assert.Equal(0, await shop.CountAsync("SELECT count(*) FROM orders.outbox"));
        Assert.Equal(0, await shop.ShipmentsAsync());
    }

    [Fact]
    public async Task Relay_dies_after_publishing_the_message_goes_out_twice_and_is_shipped_once()
    {
        await using var shop = await ShopSystem.StartAsync(Design.Outbox);
        var crashes = 1;
        shop.Faults.At("after-publish", () => crashes-- > 0 ? new SimulatedCrash("after-publish") : null);

        await shop.Orders.PlaceOrderAsync("anna@example.com", 100m);

        Assert.True(await ShopSystem.EventuallyAsync(async () => shop.Publisher.Published == 2));
        await ShopSystem.SettleAsync();
        output.WriteLine($"OUTBOX published {shop.Publisher.Published} times, delivered {shop.Consumer.Deliveries} times, shipments {await shop.ShipmentsAsync()}");
        Assert.Equal(1, await shop.ShipmentsAsync());
    }

    [Fact]
    public async Task Consumer_dies_before_acknowledging_the_message_is_delivered_again_and_shipped_once()
    {
        await using var shop = await ShopSystem.StartAsync(Design.Outbox);
        var crashes = 1;
        shop.Faults.At("before-ack", () => crashes-- > 0 ? new SimulatedCrash("before-ack") : null);

        await shop.Orders.PlaceOrderAsync("anna@example.com", 100m);

        Assert.True(await ShopSystem.EventuallyAsync(async () => shop.Consumer.Deliveries == 2));
        await ShopSystem.SettleAsync();
        Assert.Equal(1, await shop.ShipmentsAsync());
    }

    [Fact]
    public async Task Three_relays_at_once_ship_every_order_once()
    {
        await using var shop = await ShopSystem.StartAsync(Design.Outbox, relays: 3);

        await Task.WhenAll(Enumerable.Range(1, 100).Select(n => shop.Orders.PlaceOrderAsync($"customer-{n}@example.com", n)));

        Assert.True(await ShopSystem.EventuallyAsync(async () => await shop.ShipmentsAsync() == 100));
        await ShopSystem.SettleAsync();
        output.WriteLine($"RELAYS published {shop.Publisher.Published} messages for 100 orders");
        Assert.Equal(100, await shop.ShipmentsAsync());
        Assert.Equal(0, await shop.OrdersShippedMoreThanOnceAsync());
        Assert.Equal(100, shop.Publisher.Published);   // SKIP LOCKED: no relay takes a row another one holds
    }
}

/// <summary>The same stream of orders and the same mix of failures against both designs.</summary>
public class Under_failures(ITestOutputHelper output)
{
    private async Task<(long Orders, long Shipments, long NeverShipped, long ShippedTwice, int Errors)> RunAsync(Design design)
    {
        await using var shop = await ShopSystem.StartAsync(design);
        var random = new Random(2026);
        bool Chance(double p) { lock (random) return random.NextDouble() < p; }

        shop.Faults.At("publish", () => Chance(0.15) ? new BrokerUnavailableException() : null);     // the broker has a bad moment
        shop.Faults.At("after-commit", () => Chance(0.03) ? new SimulatedCrash("after-commit") : null);
        shop.Faults.At("after-publish", () => Chance(0.05) ? new SimulatedCrash("after-publish") : null);
        shop.Faults.At("before-ack", () => Chance(0.05) ? new SimulatedCrash("before-ack") : null);

        var errors = 0;
        for (var n = 1; n <= 300; n++)
        {
            try { await shop.Orders.PlaceOrderAsync($"customer-{n}@example.com", n); }
            catch (Exception e) when (e is BrokerUnavailableException or SimulatedCrash) { errors++; }
        }

        shop.Faults.Clear();   // everything is healthy again; give the system time to catch up
        await ShopSystem.EventuallyAsync(async () => await shop.OrdersWithoutShipmentAsync() == 0, seconds: 15);
        await ShopSystem.SettleAsync();

        var result = (await shop.OrdersAsync(), await shop.ShipmentsAsync(), await shop.OrdersWithoutShipmentAsync(), await shop.OrdersShippedMoreThanOnceAsync(), errors);
        output.WriteLine($"FAILURES {design}: {errors} of 300 requests returned an error, {result.Item1} orders stored, {result.Item2} shipments, {result.Item3} orders never shipped, {result.Item4} orders shipped more than once");
        return result;
    }

    [Fact]
    public async Task Dual_write_loses_shipments_and_duplicates_others()
    {
        var result = await RunAsync(Design.DualWrite);

        Assert.True(result.NeverShipped > 0);
        Assert.True(result.ShippedTwice > 0);
        Assert.Equal(300, result.Orders);   // every request stored an order, including the ones that reported an error
        Assert.True(result.Errors > 0);
    }

    [Fact]
    public async Task Outbox_ships_every_order_exactly_once()
    {
        var result = await RunAsync(Design.Outbox);

        Assert.Equal(0, result.Errors);
        Assert.Equal(300, result.Orders);
        Assert.Equal(300, result.Shipments);
        Assert.Equal(0, result.NeverShipped);
        Assert.Equal(0, result.ShippedTwice);
    }
}
