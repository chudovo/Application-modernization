using Xunit;
using Xunit.Abstractions;

namespace Shop.Tests;

/// <summary>What the first version after the split does when a machine fails. These tests describe it; they do not approve of it.</summary>
public class First_attempt_when_something_fails
{
    private static readonly Line[] TwoChairs = [new("CHAIR", 2)];

    [Fact]
    public async Task Process_dies_after_reserving_the_goods_are_gone_and_the_order_hangs_forever()
    {
        await using var shop = await ShopSystem.StartAsync(Design.FirstAttempt);
        shop.Faults.At("after-stock", () => Fault.Crash);

        await Assert.ThrowsAsync<SimulatedCrash>(() => shop.Orders.PlaceOrderAsync(Guid.NewGuid(), "anna@example.com", "visa", TwoChairs));
        shop.Faults.Clear();
        await shop.Orders.SettleAsync();   // nothing knows there is something to finish

        Assert.Equal(new Books(Confirmed: 0, Rejected: 0, Unfinished: 1, UnitsMissingFromStock: 2, 0, 0, 0), await shop.BooksAsync());
    }

    [Fact]
    public async Task Process_dies_after_charging_the_customer_has_paid_for_an_order_that_is_not_confirmed()
    {
        await using var shop = await ShopSystem.StartAsync(Design.FirstAttempt);
        shop.Faults.At("after-payment", () => Fault.Crash);

        await Assert.ThrowsAsync<SimulatedCrash>(() => shop.Orders.PlaceOrderAsync(Guid.NewGuid(), "anna@example.com", "visa", TwoChairs));

        var books = await shop.BooksAsync();
        Assert.Equal(1, books.PaidButNotConfirmed);
        Assert.Equal(2, books.UnitsMissingFromStock);
    }

    [Fact]
    public async Task Payment_answers_too_late_the_retry_charges_the_customer_twice()
    {
        await using var shop = await ShopSystem.StartAsync(Design.FirstAttempt);
        var late = 1;
        shop.Faults.At("payments.capture", () => late-- > 0 ? Fault.AnsweredTooLate : Fault.None);

        var outcome = await shop.Orders.PlaceOrderAsync(Guid.NewGuid(), "anna@example.com", "visa", TwoChairs);

        Assert.Equal(OrderStatus.Confirmed, outcome.Status);   // everything looks fine
        Assert.Equal(1, (await shop.BooksAsync()).ChargedTwice);
    }

    [Fact]
    public async Task Undoing_fails_too_and_the_goods_of_a_declined_order_stay_reserved()
    {
        await using var shop = await ShopSystem.StartAsync(Design.FirstAttempt);
        shop.Faults.At("inventory.release", () => Fault.Unavailable);

        await Assert.ThrowsAsync<ServiceUnavailableException>(() => shop.Orders.PlaceOrderAsync(Guid.NewGuid(), "anna@example.com", "declined", TwoChairs));

        Assert.Equal(new Books(Confirmed: 0, Rejected: 0, Unfinished: 1, UnitsMissingFromStock: 2, 0, 0, 0), await shop.BooksAsync());
    }
}

public class Saga_when_something_fails
{
    private static readonly Line[] TwoChairs = [new("CHAIR", 2)];
    private static readonly Books OneConfirmedOrder = new(Confirmed: 1, Rejected: 0, 0, 0, 0, 0, 0);
    private static readonly Books OneRejectedOrder = new(Confirmed: 0, Rejected: 1, 0, 0, 0, 0, 0);

    [Fact]
    public async Task Process_dies_after_reserving_and_the_next_one_finishes_the_order()
    {
        await using var shop = await ShopSystem.StartAsync(Design.Saga);
        var crashes = 1;
        shop.Faults.At("after-stock", () => crashes-- > 0 ? Fault.Crash : Fault.None);

        await Assert.ThrowsAsync<SimulatedCrash>(() => shop.Orders.PlaceOrderAsync(Guid.NewGuid(), "anna@example.com", "visa", TwoChairs));
        Assert.Equal(1, (await shop.BooksAsync()).Unfinished);

        await shop.Orders.SettleAsync();

        Assert.Equal(OneConfirmedOrder, await shop.BooksAsync());
        Assert.Equal(8, await shop.StockAsync("CHAIR"));   // reserved once, although the step ran twice
    }

    [Fact]
    public async Task Process_dies_after_charging_and_the_order_is_confirmed_with_one_charge()
    {
        await using var shop = await ShopSystem.StartAsync(Design.Saga);
        var crashes = 1;
        shop.Faults.At("after-payment", () => crashes-- > 0 ? Fault.Crash : Fault.None);

        await Assert.ThrowsAsync<SimulatedCrash>(() => shop.Orders.PlaceOrderAsync(Guid.NewGuid(), "anna@example.com", "visa", TwoChairs));
        await shop.Orders.SettleAsync();

        Assert.Equal(OneConfirmedOrder, await shop.BooksAsync());
    }

    [Fact]
    public async Task Payment_answers_too_late_the_order_waits_and_is_charged_once()
    {
        await using var shop = await ShopSystem.StartAsync(Design.Saga);
        var late = 1;
        shop.Faults.At("payments.capture", () => late-- > 0 ? Fault.AnsweredTooLate : Fault.None);

        var outcome = await shop.Orders.PlaceOrderAsync(Guid.NewGuid(), "anna@example.com", "visa", TwoChairs);
        Assert.Equal(OrderStatus.Pending, outcome.Status);   // the caller is told the truth: not decided yet

        await shop.Orders.SettleAsync();

        Assert.Equal(OneConfirmedOrder, await shop.BooksAsync());
    }

    [Fact]
    public async Task Payment_stays_unreachable_the_order_is_given_up_and_the_goods_go_back()
    {
        await using var shop = await ShopSystem.StartAsync(Design.Saga);
        shop.Faults.At("payments.capture", () => Fault.Unavailable);

        await shop.Orders.PlaceOrderAsync(Guid.NewGuid(), "anna@example.com", "visa", TwoChairs);
        await shop.Orders.SettleAsync();

        Assert.Equal(OneRejectedOrder, await shop.BooksAsync());
        Assert.Equal(10, await shop.StockAsync("CHAIR"));
    }

    [Fact]
    public async Task Payment_was_taken_but_never_confirmed_to_us_the_order_is_given_up_and_the_money_goes_back()
    {
        await using var shop = await ShopSystem.StartAsync(Design.Saga);
        shop.Faults.At("payments.capture", () => Fault.AnsweredTooLate);   // every time: charged, and we never hear of it

        await shop.Orders.PlaceOrderAsync(Guid.NewGuid(), "anna@example.com", "visa", TwoChairs);
        await shop.Orders.SettleAsync();

        Assert.Equal(OneRejectedOrder, await shop.BooksAsync());   // PaidButNotConfirmed is 0: the capture was refunded
        Assert.Equal(10, await shop.StockAsync("CHAIR"));
    }

    [Fact]
    public async Task Undoing_fails_three_times_and_is_repeated_until_the_goods_are_back()
    {
        await using var shop = await ShopSystem.StartAsync(Design.Saga);
        var failures = 3;
        shop.Faults.At("inventory.release", () => failures-- > 0 ? Fault.Unavailable : Fault.None);

        var outcome = await shop.Orders.PlaceOrderAsync(Guid.NewGuid(), "anna@example.com", "declined", TwoChairs);
        Assert.Equal(OrderStatus.Pending, outcome.Status);

        await shop.Orders.SettleAsync();

        Assert.Equal(OneRejectedOrder, await shop.BooksAsync());
        Assert.Equal(10, await shop.StockAsync("CHAIR"));
    }
}

/// <summary>The same 300 orders and the same mix of failures against all three designs.</summary>
public class Under_failures(ITestOutputHelper output)
{
    private async Task<Books> RunAsync(Design design)
    {
        await using var shop = await ShopSystem.StartAsync(design);
        await shop.SetStockAsync("CHAIR", 100_000);
        var random = new Random(2026);
        Fault Sometimes(double unavailable, double late = 0, double crash = 0)
        {
            var roll = random.NextDouble();
            return roll < unavailable ? Fault.Unavailable : roll < unavailable + late ? Fault.AnsweredTooLate : roll < unavailable + late + crash ? Fault.Crash : Fault.None;
        }

        shop.Faults.At("inventory.reserve", () => Sometimes(0.04, late: 0.04));
        shop.Faults.At("payments.capture", () => Sometimes(0.04, late: 0.04));
        shop.Faults.At("inventory.release", () => Sometimes(0.10));
        shop.Faults.At("payments.refund", () => Sometimes(0.10));
        shop.Faults.At("after-stock", () => Sometimes(0, crash: 0.02));
        shop.Faults.At("after-payment", () => Sometimes(0, crash: 0.02));

        var errors = 0;
        for (var n = 1; n <= 300; n++)
        {
            var card = random.NextDouble() < 0.10 ? "declined" : "visa";
            try { await shop.Orders.PlaceOrderAsync(Guid.NewGuid(), $"customer-{n}@example.com", card, [new("CHAIR", random.Next(1, 4))]); }
            catch (Exception e) when (e is SimulatedCrash or ServiceUnavailableException) { errors++; }
        }

        shop.Faults.Clear();          // every machine is healthy again
        await shop.Orders.SettleAsync();

        var books = await shop.BooksAsync();
        output.WriteLine($"FAILURES {design}: {errors} of 300 requests ended in an error; confirmed {books.Confirmed}, rejected {books.Rejected}, unfinished {books.Unfinished}; " +
                         $"units missing from stock {books.UnitsMissingFromStock}, paid but not confirmed {books.PaidButNotConfirmed}, confirmed but not paid {books.ConfirmedButNotPaid}, charged twice {books.ChargedTwice}");
        return books;
    }

    [Fact]
    public async Task The_monolith_stays_consistent() => Assert.True((await RunAsync(Design.Monolith)).Consistent);

    [Fact]
    public async Task The_saga_ends_consistent_with_every_order_decided()
    {
        var books = await RunAsync(Design.Saga);

        Assert.True(books.Consistent);
        Assert.Equal(300, books.Confirmed + books.Rejected);
    }

    [Fact]
    public async Task The_first_attempt_does_not()
    {
        var books = await RunAsync(Design.FirstAttempt);

        Assert.False(books.Consistent);
        Assert.True(books.Unfinished > 0);
        Assert.True(books.UnitsMissingFromStock > 0);
        Assert.True(books.ChargedTwice > 0);
    }
}
