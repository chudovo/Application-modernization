using Xunit;

namespace Shop.Tests;

/// <summary>What the business sees when nothing goes wrong. Both designs must pass these unchanged.</summary>
public abstract class Scenarios(Design design)
{
    [Fact]
    public async Task A_placed_order_is_stored_and_gets_exactly_one_shipment()
    {
        await using var shop = await ShopSystem.StartAsync(design);

        var orderId = await shop.Orders.PlaceOrderAsync("anna@example.com", 707.00m);

        Assert.True(await ShopSystem.EventuallyAsync(async () => await shop.ShipmentsAsync() == 1));
        Assert.Equal(1, await shop.CountAsync($"SELECT count(*) FROM orders.orders WHERE id = '{orderId}' AND total = 707.00"));
        Assert.Equal(1, await shop.CountAsync($"SELECT count(*) FROM shipping.shipments WHERE order_id = '{orderId}' AND customer = 'anna@example.com'"));
    }

    [Fact]
    public async Task Thirty_orders_placed_at_once_get_thirty_shipments()
    {
        await using var shop = await ShopSystem.StartAsync(design);

        await Task.WhenAll(Enumerable.Range(1, 30).Select(n => shop.Orders.PlaceOrderAsync($"customer-{n}@example.com", n * 10m)));

        Assert.True(await ShopSystem.EventuallyAsync(async () => await shop.ShipmentsAsync() == 30));
        await ShopSystem.SettleAsync();
        Assert.Equal(30, await shop.ShipmentsAsync());
        Assert.Equal(0, await shop.OrdersWithoutShipmentAsync());
        Assert.Equal(0, await shop.OrdersShippedMoreThanOnceAsync());
    }

    [Theory]
    [InlineData("", 10)]
    [InlineData("anna@example.com", 0)]
    public async Task An_invalid_order_is_refused_and_leaves_no_trace(string customer, decimal total)
    {
        await using var shop = await ShopSystem.StartAsync(design);

        await Assert.ThrowsAsync<ArgumentException>(() => shop.Orders.PlaceOrderAsync(customer, total));

        await ShopSystem.SettleAsync();
        Assert.Equal(0, await shop.OrdersAsync());
        Assert.Equal(0, await shop.ShipmentsAsync());
    }
}

public class Scenarios_with_dual_write() : Scenarios(Design.DualWrite);
public class Scenarios_with_outbox() : Scenarios(Design.Outbox);
