using OrderDesk.Domain;

namespace OrderDesk.Api;

public class CreateOrderRequest
{
    public int CustomerId { get; set; }
    public List<CreateOrderLine>? Lines { get; set; }
}

public class CreateOrderLine
{
    public string? Sku { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public record ErrorBody(string Message);

public record OrderLineDto(string Sku, int Quantity, decimal UnitPrice, decimal LineTotal);

public record OrderDto(
    int Id, string CustomerName, OrderStatus Status, DateTime CreatedUtc, List<OrderLineDto> Lines,
    decimal Subtotal, decimal DiscountPercent, decimal NetTotal, decimal VatTotal, decimal GrossTotal)
{
    public static OrderDto From(Order order) => new(
        order.Id,
        order.Customer.Name,
        order.Status,
        // The database returns DateTime with Kind=Unspecified; without this the "Z" suffix disappears from the JSON.
        DateTime.SpecifyKind(order.CreatedUtc, DateTimeKind.Utc),
        [.. order.Lines.OrderBy(l => l.Id).Select(l => new OrderLineDto(l.Sku, l.Quantity, l.UnitPrice, l.Quantity * l.UnitPrice))],
        order.Subtotal, order.DiscountPercent, order.NetTotal, order.VatTotal, order.GrossTotal);
}
