using OrderDesk.Data;
using Xunit;

namespace OrderDesk.Scenarios;

/// <summary>
/// What the order desk does with its database. This file is compiled into two test projects: one references the
/// Entity Framework 6 data layer, the other the EF Core one. Every test gets a fresh copy of the existing database.
/// </summary>
public class Scenarios : IDisposable
{
    private readonly TestDatabase _database = TestDatabase.Create();
    private readonly OrderStore _store;

    public Scenarios() => _store = new OrderStore(_database.ConnectionString);
    public void Dispose() => _database.Dispose();

    private static NewLine Line(string sku, int quantity, decimal unitPrice) => new(sku, quantity, unitPrice);
    private static readonly DateTime Noon = new(2026, 3, 5, 12, 0, 0);

    [Fact]
    public void Existing_order_is_read_with_customer_lines_and_tags()
    {
        var order = _store.GetOrder(1);

        Assert.Equal("Nordwind Handel GmbH", order.CustomerName);
        Assert.Equal("Hamburg", order.City);
        Assert.Equal(new DateTime(2026, 3, 2, 9, 15, 0, 123), order.CreatedUtc);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(707.00m, order.Total);
        Assert.Equal(["CHAIR-01 x4 @89.50", "DESK-02 x1 @349.00"], order.Lines.Select(l => $"{l.Sku} x{l.Quantity} @{l.UnitPrice}"));
        Assert.Equal(["priority", "wholesale"], order.Tags);
    }

    [Fact]
    public void Missing_order_is_reported_as_not_found()
    {
        Assert.Throws<NotFoundException>(() => _store.GetOrder(999));
    }

    [Fact]
    public void Existing_customer_is_read_with_address_and_order_count()
    {
        var customer = _store.GetCustomer(1);

        Assert.Equal("Nordwind Handel GmbH", customer.Name);
        Assert.Equal("Speicherstraße 12, 20457 Hamburg, DE", $"{customer.Street}, {customer.PostalCode} {customer.City}, {customer.CountryCode}");
        Assert.Equal(2, customer.OrderCount);
    }

    [Fact]
    public void Customer_without_street_and_postal_code_is_read()
    {
        var customer = _store.GetCustomer(3);

        Assert.Null(customer.Street);
        Assert.Null(customer.PostalCode);
        Assert.Equal("Gdańsk", customer.City);
        Assert.Equal(0, customer.OrderCount);
    }

    [Fact]
    public void New_customer_is_stored_with_its_address()
    {
        var id = _store.AddCustomer("Van Dijk BV", "info@vandijk.example", "Herengracht 5", "Amsterdam", "1015 BA", "NL");

        Assert.Equal(
            ["Van Dijk BV | info@vandijk.example | Herengracht 5 | Amsterdam | 1015 BA | NL | True"],
            _database.Rows($"SELECT Name, Email, Address_Street, Address_City, Address_PostalCode, Address_CountryCode, IsActive FROM dbo.Customers WHERE Id = {id}"));
    }

    [Fact]
    public void New_order_is_stored_with_lines_total_and_tags()
    {
        var id = _store.PlaceOrder(new NewOrder(2, Noon, [Line("LAMP-07", 3, 39.99m), Line("SHELF-03", 1, 120.00m)], "priority", "export"));

        var order = _store.GetOrder(id);
        Assert.Equal("Atelier Dubois", order.CustomerName);
        Assert.Equal(OrderStatus.New, order.Status);
        Assert.Equal(239.97m, order.Total);
        Assert.Equal(2, order.Lines.Count);
        Assert.Equal(["export", "priority"], order.Tags);

        // The existing tag is reused, the new one is created once.
        Assert.Equal(["export", "priority", "wholesale"], _database.Rows("SELECT Name FROM dbo.Tags ORDER BY Name"));
        Assert.Equal($"{id} | 2", _database.Rows($"SELECT Id, Customer_Id FROM dbo.Orders WHERE Id = {id}").Single());
    }

    [Fact]
    public void Tagging_an_order_makes_it_findable_by_tag()
    {
        Assert.Equal([1, 4], _store.FindOrderIdsByTag("priority"));

        _store.TagOrder(2, "priority");
        _store.TagOrder(2, "priority");

        Assert.Equal([1, 2, 4], _store.FindOrderIdsByTag("priority"));
        Assert.Equal(2, _database.Scalar<int>("SELECT COUNT(*) FROM dbo.TagOrders WHERE Order_Id = 2"));
    }

    [Fact]
    public void Status_change_with_the_current_row_version_succeeds_and_changes_the_version()
    {
        var before = _store.GetOrder(2);

        _store.ChangeStatus(2, OrderStatus.Confirmed, before.RowVersion);

        var after = _store.GetOrder(2);
        Assert.Equal(OrderStatus.Confirmed, after.Status);
        Assert.NotEqual(before.RowVersion, after.RowVersion);
    }

    [Fact]
    public void Status_change_with_a_stale_row_version_is_reported_as_a_conflict()
    {
        var read = _store.GetOrder(2);
        _database.Execute("UPDATE dbo.Orders SET Note = N'Changed by someone else' WHERE Id = 2");

        Assert.Throws<ConcurrencyConflictException>(() => _store.ChangeStatus(2, OrderStatus.Cancelled, read.RowVersion));

        Assert.Equal(OrderStatus.New, _store.GetOrder(2).Status);
    }

    [Fact]
    public void Deleting_an_order_removes_its_lines_and_tag_links_but_not_the_tags()
    {
        _store.DeleteOrder(1);

        Assert.Throws<NotFoundException>(() => _store.GetOrder(1));
        Assert.Equal(0, _store.CountLines(1));
        Assert.Equal(0, _database.Scalar<int>("SELECT COUNT(*) FROM dbo.TagOrders WHERE Order_Id = 1"));
        Assert.Equal(2, _database.Scalar<int>("SELECT COUNT(*) FROM dbo.Tags"));
    }

    [Fact]
    public void Revenue_by_customer_for_a_day_excludes_cancelled_orders()
    {
        var revenue = _store.RevenueByCustomer(new DateTime(2026, 3, 2, 18, 30, 0));

        // Atelier Dubois has one order on that day and it is cancelled.
        Assert.Equal(["Nordwind Handel GmbH: 2 orders, 786.98"], revenue.Select(r => $"{r.CustomerName}: {r.Orders} orders, {r.Revenue}"));
    }

    [Fact]
    public void Recent_orders_come_from_hand_written_sql()
    {
        var recent = _store.RecentOrders(2);

        Assert.Equal(
            ["4 Atelier Dubois 360.00 lines:1", "3 Atelier Dubois 349.00 lines:1"],
            recent.Select(o => $"{o.Id} {o.CustomerName} {o.Total} lines:{o.LineCount}"));
        Assert.Equal(new DateTime(2026, 3, 3, 8, 0, 0), recent[0].CreatedUtc);
    }

    [Fact]
    public void Customer_search_looks_at_name_and_city_of_active_customers_and_pages_by_name()
    {
        Assert.Equal(["Atelier Dubois"], _store.SearchCustomers("o", page: 1, pageSize: 1));
        Assert.Equal(["Nordwind Handel GmbH"], _store.SearchCustomers("o", page: 2, pageSize: 1));
        Assert.Equal(["Atelier Dubois"], _store.SearchCustomers("lyon", page: 1, pageSize: 10));
        Assert.Empty(_store.SearchCustomers("Kowalski", page: 1, pageSize: 10));
    }

    [Fact]
    public void A_failed_import_stores_nothing()
    {
        NewOrder[] batch =
        [
            new(1, Noon, [Line("CHAIR-01", 1, 89.50m)], "import"),
            new(999, Noon, [Line("CHAIR-01", 1, 89.50m)]),
        ];

        Assert.Throws<NotFoundException>(() => _store.ImportOrders(batch));

        Assert.Equal(4, _database.Scalar<int>("SELECT COUNT(*) FROM dbo.Orders"));
        Assert.Equal(0, _database.Scalar<int>("SELECT COUNT(*) FROM dbo.Tags WHERE Name = N'import'"));
    }

    [Fact]
    public void A_successful_import_stores_every_order()
    {
        var ids = _store.ImportOrders(
        [
            new(1, Noon, [Line("CHAIR-01", 1, 89.50m)], "import"),
            new(2, Noon, [Line("DESK-02", 2, 349.00m)], "import"),
        ]);

        Assert.Equal(2, ids.Count);
        Assert.Equal(ids, _store.FindOrderIdsByTag("import"));
        Assert.Equal(698.00m, _store.GetOrder(ids[1]).Total);
    }

    [Fact]
    public void A_price_with_three_decimals_is_stored_with_two()
    {
        var id = _store.PlaceOrder(new NewOrder(1, Noon, [Line("CABLE-09", 1, 19.999m)]));

        Assert.Equal(PRICE_AS_STORED, _store.GetOrder(id).Lines.Single().UnitPrice);
    }
    // Not rounded to 20.00: the value is cut off after the second decimal. This is what the old application has always done.
    private const decimal PRICE_AS_STORED = 19.99m;

    [Fact]
    public void An_order_is_found_again_by_its_creation_time()
    {
        // Whole seconds only. With milliseconds the two versions differ, see the version-specific tests.
        var created = new DateTime(2026, 3, 5, 10, 0, 7);
        var id = _store.PlaceOrder(new NewOrder(1, created, [Line("CHAIR-01", 1, 89.50m)]));

        Assert.Equal([id], _store.FindOrderIdsCreatedAt(created));
    }
}
