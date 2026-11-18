using System.Net;
using System.Net.Http.Json;

namespace Shop.Monolith;

/// <summary>What the order code needs to know about a product. Nothing else of the catalog crosses this line.</summary>
public record CatalogProduct(string Sku, string Name, decimal Price, bool IsActive);

/// <summary>
/// The seam. Before the extraction the order code read the Products table itself. Now it asks this interface, and
/// configuration decides whether the answer comes from the local table or from the catalog service.
/// </summary>
public interface ICatalog
{
    Task<CatalogProduct?> FindAsync(string sku);
}

public class LocalCatalog(ShopContext db) : ICatalog
{
    public async Task<CatalogProduct?> FindAsync(string sku) =>
        await db.Products.FindAsync(sku) is { } p ? new CatalogProduct(p.Sku, p.Name, p.Price, p.IsActive) : null;
}

public class RemoteCatalog(HttpClient http) : ICatalog
{
    public async Task<CatalogProduct?> FindAsync(string sku)
    {
        try
        {
            using var response = await http.GetAsync($"/api/products/{Uri.EscapeDataString(sku)}");
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<CatalogProduct>();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // What used to be a method call can now fail on its own.
            throw new CatalogUnavailableException(e);
        }
    }
}

public class CatalogUnavailableException(Exception inner) : Exception("The catalog service did not answer.", inner);
