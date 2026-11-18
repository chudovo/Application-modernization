using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Shop.Catalog;
using Shop.Gateway;
using Xunit;

namespace Shop.Contract;

/// <summary>What is specific to the state with the catalog extracted. These have no counterpart in the monolith.</summary>
public class ExtractionTests : IAsyncLifetime
{
    private Topology _shop = null!;
    public async Task InitializeAsync() => _shop = await Topology.StartAsync(catalogExtracted: true);
    public async Task DisposeAsync() => await _shop.DisposeAsync();

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    private const string NewRug = """{"sku":"RUG-11","name":"Wool rug","price":210.00}""";
    private const string OrderOneChair = """{"customerEmail":"anna@example.com","lines":[{"sku":"CHAIR-01","quantity":1}]}""";

    [Fact]
    public async Task Catalog_requests_reach_the_new_service_and_no_longer_the_monolith()
    {
        await _shop.Client.PostAsync("/api/products", Json(NewRug));

        Assert.Equal(HttpStatusCode.OK, (await _shop.Client.GetAsync("/api/products/RUG-11")).StatusCode);
        // Asked directly, the monolith does not know the product: its own table was not written.
        Assert.Equal(HttpStatusCode.NotFound, (await _shop.MonolithDirect.GetAsync("/api/products/RUG-11")).StatusCode);
    }

    [Fact]
    public async Task The_copy_at_cutover_brings_every_product_and_can_be_repeated()
    {
        // A price is changed in the monolith after the first copy, as happens between a rehearsal and the real switch.
        await _shop.MonolithDirect.PutAsync("/api/products/LAMP-07/price", Json("""{"price":44.00}"""));

        using var scope = _shop.Catalog!.Services.CreateScope();
        var copied = await scope.ServiceProvider.GetRequiredService<Backfill>().RunAsync(_shop.MonolithDirect.BaseAddress!.ToString(), "demo-key");

        Assert.Equal(4, copied);
        Assert.Equal("""{"sku":"LAMP-07","name":"Desk lamp","price":44.00,"isActive":true}""",
            await (await _shop.Client.GetAsync("/api/products/LAMP-07")).Content.ReadAsStringAsync());
        Assert.Equal(4, System.Text.Json.JsonDocument.Parse(await (await _shop.Client.GetAsync("/api/products")).Content.ReadAsStringAsync()).RootElement.GetArrayLength());
    }

    [Fact]
    public async Task When_the_catalog_service_is_down_orders_can_be_read_but_not_placed()
    {
        await _shop.Client.PostAsync("/api/orders", Json(OrderOneChair));

        await _shop.Catalog!.StopAsync();

        // The parts of the monolith that do not need the catalog keep working.
        Assert.Equal(HttpStatusCode.OK, (await _shop.Client.GetAsync("/api/orders/1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _shop.Client.GetAsync("/api/stock/CHAIR-01")).StatusCode);

        // Placing an order needs a price. The monolith says what is wrong and changes nothing.
        var order = await _shop.Client.PostAsync("/api/orders", Json(OrderOneChair));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, order.StatusCode);
        Assert.Equal("""{"error":"Orders cannot be placed right now: the catalog is unavailable."}""", await order.Content.ReadAsStringAsync());
        Assert.Equal("""{"sku":"CHAIR-01","quantity":9}""", await (await _shop.Client.GetAsync("/api/stock/CHAIR-01")).Content.ReadAsStringAsync());

        // The gateway answers for the catalog paths itself.
        Assert.Equal(HttpStatusCode.BadGateway, (await _shop.Client.GetAsync("/api/products")).StatusCode);
    }

    [Fact]
    public async Task Routing_back_to_the_monolith_is_immediate_but_data_written_since_the_cutover_stays_behind()
    {
        await _shop.Client.PostAsync("/api/products", Json(NewRug));
        await _shop.Client.PutAsync("/api/products/CHAIR-01/price", Json("""{"price":95.00}"""));

        _shop.Routing.RouteCatalogTo(Owner.Monolith);

        // The very next request is answered by the monolith again, without a restart.
        Assert.Equal(HttpStatusCode.OK, (await _shop.Client.GetAsync("/api/products/DESK-02")).StatusCode);

        // But the monolith's table stopped at the cutover: the new product and the new price are not there.
        Assert.Equal(HttpStatusCode.NotFound, (await _shop.Client.GetAsync("/api/products/RUG-11")).StatusCode);
        Assert.Contains("\"price\":89.50", await (await _shop.Client.GetAsync("/api/products/CHAIR-01")).Content.ReadAsStringAsync());

        // And the monolith's order code still asks the service (Catalog:Mode is a second switch), so the two disagree:
        // the catalog shows 89.50, a new order is priced at 95.00.
        var order = await _shop.Client.PostAsync("/api/orders", Json(OrderOneChair));
        Assert.Contains("\"unitPrice\":95.00", await order.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Routing_forward_again_shows_the_data_of_the_new_service_untouched()
    {
        await _shop.Client.PostAsync("/api/products", Json(NewRug));
        _shop.Routing.RouteCatalogTo(Owner.Monolith);

        _shop.Routing.RouteCatalogTo(Owner.CatalogService);

        Assert.Equal(HttpStatusCode.OK, (await _shop.Client.GetAsync("/api/products/RUG-11")).StatusCode);
    }
}
