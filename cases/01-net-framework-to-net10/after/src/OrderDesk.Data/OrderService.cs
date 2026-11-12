using Microsoft.EntityFrameworkCore;
using OrderDesk.Domain;

namespace OrderDesk.Data;

public record NewOrderLine(string? Sku, int Quantity, decimal UnitPrice);

/// <summary>
/// The context, the pricing rules and the clock are injected. The context lives for one request,
/// and navigation properties are loaded explicitly with Include.
/// </summary>
public class OrderService(OrderDeskContext db, PricingService pricing, TimeProvider clock)
{
    private IQueryable<Order> OrdersWithDetails => db.Orders.Include(o => o.Customer).Include(o => o.Lines);

    public Task<Order?> GetAsync(int id, CancellationToken ct) =>
        OrdersWithDetails.SingleOrDefaultAsync(o => o.Id == id, ct);

    public Task<List<Order>> FindAsync(int? customerId, OrderStatus? status, CancellationToken ct)
    {
        var query = OrdersWithDetails;
        if (customerId.HasValue) query = query.Where(o => o.CustomerId == customerId.Value);
        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        return query.OrderBy(o => o.Id).ToListAsync(ct);
    }

    public async Task<Order> CreateAsync(int customerId, IReadOnlyList<NewOrderLine> lines, CancellationToken ct)
    {
        if (lines.Count == 0)
            throw new OrderValidationException("Order must contain at least one line.");
        if (lines.Any(l => l.Quantity <= 0 || l.UnitPrice < 0 || string.IsNullOrWhiteSpace(l.Sku)))
            throw new OrderValidationException("Every line needs a SKU, a positive quantity and a non-negative price.");

        var customer = await db.Customers.FindAsync([customerId], ct)
            ?? throw new OrderValidationException($"Customer {customerId} does not exist.");

        var order = new Order
        {
            CustomerId = customer.Id,
            Customer = customer,
            Status = OrderStatus.New,
            CreatedUtc = clock.GetUtcNow().UtcDateTime,
            Lines = [.. lines.Select(l => new OrderLine { Sku = l.Sku!, Quantity = l.Quantity, UnitPrice = l.UnitPrice })]
        };

        pricing.Price(order, customer);
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        return order;
    }

    public async Task<Order?> CancelAsync(int id, CancellationToken ct)
    {
        var order = await GetAsync(id, ct);
        if (order is null) return null;
        if (order.Status == OrderStatus.Shipped)
            throw new OrderConflictException($"Order {id} is already shipped and cannot be cancelled.");

        order.Status = OrderStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        return order;
    }
}
