using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Shop.Contract;

/// <summary>Whatever is listening at <see cref="Client"/>.BaseAddress: the monolith alone, or the gateway in front of it.</summary>
public interface IRunningShop : IAsyncDisposable
{
    HttpClient Client { get; }
}

/// <summary>
/// What the clients of the shop rely on, written against HTTP only. The same scenarios run against the monolith, against
/// the gateway that forwards everything to the monolith, and against the gateway with the catalog served by a new
/// service. A subclass says which of those is started.
/// </summary>
public abstract class ContractScenarios : IAsyncLifetime
{
    protected abstract Task<IRunningShop> StartAsync();

    private IRunningShop _shop = null!;
    private HttpClient Http => _shop.Client;

    public async Task InitializeAsync() => _shop = await StartAsync();
    public async Task DisposeAsync() => await _shop.DisposeAsync();

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    private static async Task<string> Body(HttpResponseMessage response) => await response.Content.ReadAsStringAsync();

    private static async Task<JsonElement> JsonBody(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    // ---------- catalog ----------

    [Fact]
    public async Task Product_list_is_sorted_by_sku_and_has_the_agreed_shape()
    {
        var response = await Http.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal(
            """[{"sku":"CHAIR-01","name":"Oak chair","price":89.50,"isActive":true},{"sku":"DESK-02","name":"Standing desk","price":349.00,"isActive":true},{"sku":"LAMP-07","name":"Desk lamp","price":39.99,"isActive":true},{"sku":"SHELF-03","name":"Wall shelf","price":120.00,"isActive":false}]""",
            await Body(response));
    }

    [Fact]
    public async Task Product_search_matches_part_of_the_name_ignoring_case()
    {
        var products = await JsonBody(await Http.GetAsync("/api/products?search=DESK"));

        Assert.Equal(["DESK-02", "LAMP-07"], products.EnumerateArray().Select(p => p.GetProperty("sku").GetString()));
    }

    [Fact]
    public async Task Single_product_is_returned_by_sku()
    {
        var response = await Http.GetAsync("/api/products/LAMP-07");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"sku":"LAMP-07","name":"Desk lamp","price":39.99,"isActive":true}""", await Body(response));
    }

    [Fact]
    public async Task Unknown_product_is_a_404_with_an_error_message()
    {
        var response = await Http.GetAsync("/api/products/NOPE-00");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("""{"error":"Product NOPE-00 was not found."}""", await Body(response));
    }

    [Fact]
    public async Task New_product_is_created_with_a_location_the_client_can_follow()
    {
        var response = await Http.PostAsync("/api/products", Json("""{"sku":"RUG-11","name":"Wool rug","price":210.00}"""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("""{"sku":"RUG-11","name":"Wool rug","price":210.00,"isActive":true}""", await Body(response));

        // Clients follow this header as it is, so it must point at the address they talk to.
        Assert.Equal(new Uri(Http.BaseAddress!, "/api/products/RUG-11"), response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, (await Http.GetAsync(response.Headers.Location)).StatusCode);
    }

    [Fact]
    public async Task Duplicate_product_is_a_409()
    {
        var response = await Http.PostAsync("/api/products", Json("""{"sku":"CHAIR-01","name":"Another chair","price":10}"""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("""{"error":"Product CHAIR-01 already exists."}""", await Body(response));
    }

    [Theory]
    [InlineData("""{"sku":"","name":"No sku","price":10}""", "Sku and Name are required.")]
    [InlineData("""{"sku":"X-1","name":"Free","price":0}""", "Price must be greater than zero.")]
    public async Task Invalid_product_is_a_400_with_an_error_message(string request, string error)
    {
        var response = await Http.PostAsync("/api/products", Json(request));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, (await JsonBody(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Price_change_is_visible_in_the_catalog()
    {
        var response = await Http.PutAsync("/api/products/CHAIR-01/price", Json("""{"price":95.00}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"sku":"CHAIR-01","name":"Oak chair","price":95.00,"isActive":true}""", await Body(await Http.GetAsync("/api/products/CHAIR-01")));
    }

    // ---------- orders, which depend on the catalog ----------

    [Fact]
    public async Task Order_takes_names_and_prices_from_the_catalog_and_reduces_stock()
    {
        var response = await Http.PostAsync("/api/orders",
            Json("""{"customerEmail":"anna@example.com","lines":[{"sku":"CHAIR-01","quantity":4},{"sku":"DESK-02","quantity":1}]}"""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(new Uri(Http.BaseAddress!, "/api/orders/1"), response.Headers.Location);
        Assert.Equal(
            """{"id":1,"customerEmail":"anna@example.com","createdUtc":"2026-04-01T08:30:00Z","total":707.00,"lines":[{"id":1,"orderId":1,"sku":"CHAIR-01","name":"Oak chair","quantity":4,"unitPrice":89.50},{"id":2,"orderId":1,"sku":"DESK-02","name":"Standing desk","quantity":1,"unitPrice":349.00}]}""",
            await Body(response));
        Assert.Equal("""{"sku":"CHAIR-01","quantity":6}""", await Body(await Http.GetAsync("/api/stock/CHAIR-01")));
    }

    [Fact]
    public async Task Order_placed_after_a_price_change_uses_the_new_price()
    {
        await Http.PutAsync("/api/products/CHAIR-01/price", Json("""{"price":95.00}"""));

        var order = await JsonBody(await Http.PostAsync("/api/orders",
            Json("""{"customerEmail":"anna@example.com","lines":[{"sku":"CHAIR-01","quantity":2}]}""")));

        Assert.Equal(190.00m, order.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task A_product_created_a_moment_ago_can_be_ordered_once_it_has_stock()
    {
        await Http.PostAsync("/api/products", Json("""{"sku":"RUG-11","name":"Wool rug","price":210.00}"""));

        // A new product starts with a stock record of zero, so the order is refused for stock, not for the product.
        var response = await Http.PostAsync("/api/orders", Json("""{"customerEmail":"anna@example.com","lines":[{"sku":"RUG-11","quantity":1}]}"""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("""{"error":"Not enough stock for RUG-11."}""", await Body(response));
    }

    [Theory]
    [InlineData("NOPE-00")]    // not in the catalog
    [InlineData("SHELF-03")]   // in the catalog, not active
    public async Task Order_for_a_product_that_cannot_be_ordered_is_a_400(string sku)
    {
        var response = await Http.PostAsync("/api/orders",
            Json($$"""{"customerEmail":"anna@example.com","lines":[{"sku":"{{sku}}","quantity":1}]}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal($$"""{"error":"Product {{sku}} cannot be ordered."}""", await Body(response));
    }

    [Fact]
    public async Task Order_for_more_than_is_in_stock_is_a_409_and_changes_nothing()
    {
        var response = await Http.PostAsync("/api/orders",
            Json("""{"customerEmail":"anna@example.com","lines":[{"sku":"CHAIR-01","quantity":1},{"sku":"DESK-02","quantity":3}]}"""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("""{"sku":"CHAIR-01","quantity":10}""", await Body(await Http.GetAsync("/api/stock/CHAIR-01")));
        Assert.Equal(HttpStatusCode.NotFound, (await Http.GetAsync("/api/orders/1")).StatusCode);
    }

    [Fact]
    public async Task Stored_order_is_read_back()
    {
        await Http.PostAsync("/api/orders", Json("""{"customerEmail":"anna@example.com","lines":[{"sku":"DESK-02","quantity":2}]}"""));

        var order = await JsonBody(await Http.GetAsync("/api/orders/1"));

        Assert.Equal(698.00m, order.GetProperty("total").GetDecimal());
        Assert.Equal("Standing desk", order.GetProperty("lines")[0].GetProperty("name").GetString());
    }

    // ---------- cross-cutting ----------

    [Theory]
    [InlineData("/api/products")]
    [InlineData("/api/orders/1")]
    public async Task Request_without_an_api_key_is_a_401(string path)
    {
        using var anonymous = new HttpClient { BaseAddress = Http.BaseAddress };

        var response = await anonymous.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("""{"error":"A valid X-Api-Key header is required."}""", await Body(response));
    }

    [Fact]
    public async Task Health_check_needs_no_api_key()
    {
        using var anonymous = new HttpClient { BaseAddress = Http.BaseAddress };

        Assert.Equal("""{"status":"ok"}""", await Body(await anonymous.GetAsync("/api/health")));
    }
}
