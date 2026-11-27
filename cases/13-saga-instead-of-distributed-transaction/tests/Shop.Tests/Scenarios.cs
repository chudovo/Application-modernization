using Xunit;

namespace Shop.Tests;

/// <summary>What the business expects of placing an order when no machine fails. All three designs are run through these.</summary>
public abstract class Scenarios(Design design)
{
    private static readonly Line[] TwoChairsAndADesk = [new("CHAIR", 2), new("DESK", 1)];

    [Fact]
    public async Task An_order_takes_the_goods_and_the_money_and_is_confirmed()
    {
        await using var shop = await ShopSystem.StartAsync(design);

        var outcome = await shop.Orders.PlaceOrderAsync(Guid.NewGuid(), "anna@example.com", "visa", TwoChairsAndADesk);

        Assert.Equal(OrderStatus.Confirmed, outcome.Status);
        Assert.Equal(8, await shop.StockAsync("CHAIR"));
        Assert.Equal(1, await shop.StockAsync("DESK"));
        Assert.Equal(new Books(Confirmed: 1, Rejected: 0, 0, 0, 0, 0, 0), await shop.BooksAsync());
    }

    [Fact]
    public async Task A_declined_card_rejects_the_order_and_the_goods_go_back()
    {
        await using var shop = await ShopSystem.StartAsync(design);

        var outcome = await shop.Orders.PlaceOrderAsync(Guid.NewGuid(), "anna@example.com", "declined", TwoChairsAndADesk);

        Assert.Equal(OrderStatus.Rejected, outcome.Status);
        Assert.Equal("The card was declined.", outcome.Reason);
        Assert.Equal(10, await shop.StockAsync("CHAIR"));
        Assert.Equal(2, await shop.StockAsync("DESK"));
        Assert.Equal(new Books(Confirmed: 0, Rejected: 1, 0, 0, 0, 0, 0), await shop.BooksAsync());
    }

    [Fact]
    public async Task An_order_with_one_line_out_of_stock_takes_nothing_and_charges_nothing()
    {
        await using var shop = await ShopSystem.StartAsync(design);

        // Chairs are there, three desks are not.
        var outcome = await shop.Orders.PlaceOrderAsync(Guid.NewGuid(), "anna@example.com", "visa", [new("CHAIR", 2), new("DESK", 3)]);

        Assert.Equal(OrderStatus.Rejected, outcome.Status);
        Assert.Equal("Not enough stock for DESK.", outcome.Reason);
        Assert.Equal(10, await shop.StockAsync("CHAIR"));
        Assert.True((await shop.BooksAsync()).Consistent);
    }

    [Fact]
    public async Task Of_five_orders_for_the_last_lamp_exactly_one_is_confirmed()
    {
        await using var shop = await ShopSystem.StartAsync(design);

        var outcomes = await Task.WhenAll(Enumerable.Range(1, 5).Select(n => shop.Orders.PlaceOrderAsync(Guid.NewGuid(), $"customer-{n}@example.com", "visa", [new("LAMP", 1)])));

        Assert.Equal(1, outcomes.Count(o => o.Status == OrderStatus.Confirmed));
        Assert.Equal(4, outcomes.Count(o => o.Status == OrderStatus.Rejected));
        Assert.Equal(0, await shop.StockAsync("LAMP"));
        Assert.True((await shop.BooksAsync()).Consistent);
    }

    [Fact]
    public async Task The_same_request_sent_twice_is_one_order()
    {
        await using var shop = await ShopSystem.StartAsync(design);
        var requestId = Guid.NewGuid();

        var first = await shop.Orders.PlaceOrderAsync(requestId, "anna@example.com", "visa", [new("CHAIR", 1)]);
        var second = await shop.Orders.PlaceOrderAsync(requestId, "anna@example.com", "visa", [new("CHAIR", 1)]);

        Assert.Equal(first.OrderId, second.OrderId);
        Assert.Equal(9, await shop.StockAsync("CHAIR"));
        Assert.Equal(new Books(Confirmed: 1, Rejected: 0, 0, 0, 0, 0, 0), await shop.BooksAsync());
    }
}

public class Scenarios_with_the_monolith() : Scenarios(Design.Monolith);
public class Scenarios_with_the_saga() : Scenarios(Design.Saga);

// The first version after the split passes these too. Nothing is wrong with it as long as every machine answers.
public class Scenarios_with_the_first_attempt() : Scenarios(Design.FirstAttempt);
