using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Shop.Monolith.Controllers;

public record NewOrderLine(string Sku, int Quantity);
public record NewOrder(string? CustomerEmail, List<NewOrderLine>? Lines);

[ApiController, Route("api/orders")]
public class OrdersController(ShopContext db, ICatalog catalog, TimeProvider clock) : ControllerBase
{
    [HttpGet("{id:int}", Name = "GetOrder")]
    public async Task<IActionResult> Get(int id) =>
        await db.Orders.Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == id) is { } order
            ? Ok(order) : NotFound(new { error = $"Order {id} was not found." });

    [HttpPost]
    public async Task<IActionResult> Place(NewOrder request)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerEmail)) return BadRequest(new { error = "CustomerEmail is required." });
        if (request.Lines is not { Count: > 0 }) return BadRequest(new { error = "An order needs at least one line." });

        var order = new Order { CustomerEmail = request.CustomerEmail, CreatedUtc = clock.GetUtcNow().UtcDateTime };
        foreach (var line in request.Lines)
        {
            if (line.Quantity <= 0) return BadRequest(new { error = "Quantity must be greater than zero." });

            CatalogProduct? product;
            try { product = await catalog.FindAsync(line.Sku); }
            catch (CatalogUnavailableException)
            {
                // Orders cannot be priced without the catalog. Say so instead of failing with a 500.
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Orders cannot be placed right now: the catalog is unavailable." });
            }
            if (product is null || !product.IsActive) return BadRequest(new { error = $"Product {line.Sku} cannot be ordered." });

            var stock = await db.Stock.FindAsync(line.Sku);
            if (stock is null || stock.Quantity < line.Quantity)
                return Conflict(new { error = $"Not enough stock for {line.Sku}." });
            stock.Quantity -= line.Quantity;

            order.Lines.Add(new OrderLine { Sku = product.Sku, Name = product.Name, Quantity = line.Quantity, UnitPrice = product.Price });
        }
        order.Total = order.Lines.Sum(l => l.Quantity * l.UnitPrice);

        db.Orders.Add(order);
        await db.SaveChangesAsync();   // order and stock change in one transaction
        return CreatedAtRoute("GetOrder", new { id = order.Id }, order);
    }
}

[ApiController, Route("api/stock")]
public class StockController(ShopContext db) : ControllerBase
{
    [HttpGet("{sku}")]
    public async Task<IActionResult> Get(string sku) =>
        await db.Stock.FindAsync(sku) is { } item ? Ok(item) : NotFound(new { error = $"No stock record for {sku}." });
}
