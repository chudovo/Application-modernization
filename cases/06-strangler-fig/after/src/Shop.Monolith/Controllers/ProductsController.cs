using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Shop.Monolith.Controllers;

public record NewProduct(string? Sku, string? Name, decimal Price);
public record PriceChange(decimal Price);

[ApiController, Route("api/products")]
public class ProductsController(ShopContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<Product>> Find(string? search) =>
        await db.Products
            .Where(p => search == null || EF.Functions.Like(p.Name, $"%{search}%"))
            .OrderBy(p => p.Sku).ToListAsync();

    [HttpGet("{sku}", Name = "GetProduct")]
    public async Task<IActionResult> Get(string sku) =>
        await db.Products.FindAsync(sku) is { } product ? Ok(product) : NotFound(new { error = $"Product {sku} was not found." });

    [HttpPost]
    public async Task<IActionResult> Create(NewProduct request)
    {
        if (string.IsNullOrWhiteSpace(request.Sku) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Sku and Name are required." });
        if (request.Price <= 0) return BadRequest(new { error = "Price must be greater than zero." });
        if (await db.Products.FindAsync(request.Sku) is not null)
            return Conflict(new { error = $"Product {request.Sku} already exists." });

        var product = new Product { Sku = request.Sku, Name = request.Name, Price = request.Price, IsActive = true };
        db.Products.Add(product);
        db.Stock.Add(new StockItem { Sku = product.Sku, Quantity = 0 });
        await db.SaveChangesAsync();
        return CreatedAtRoute("GetProduct", new { sku = product.Sku }, product);
    }

    [HttpPut("{sku}/price")]
    public async Task<IActionResult> ChangePrice(string sku, PriceChange request)
    {
        if (request.Price <= 0) return BadRequest(new { error = "Price must be greater than zero." });
        if (await db.Products.FindAsync(sku) is not { } product) return NotFound(new { error = $"Product {sku} was not found." });
        product.Price = request.Price;
        await db.SaveChangesAsync();
        return Ok(product);
    }
}
