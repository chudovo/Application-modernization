namespace OrderDesk.Data;

// The same classes as in the Entity Framework 6 version. Navigation properties are no longer virtual: nothing loads
// on first access, each query says what it needs. How the classes map to the existing tables is in OrderDeskContext.

public class Customer
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Email { get; set; }
    public Address Address { get; set; } = new();
    public bool IsActive { get; set; }
    public List<Order> Orders { get; set; } = [];
}

public class Address
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public class Order
{
    public int Id { get; set; }
    public Customer Customer { get; set; } = null!;
    public DateTime CreatedUtc { get; set; }
    public OrderStatus Status { get; set; }
    public decimal Total { get; set; }
    public string? Note { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<OrderLine> Lines { get; set; } = [];
    public List<Tag> Tags { get; set; } = [];
}

public enum OrderStatus { New = 0, Confirmed = 1, Shipped = 2, Cancelled = 3 }

public class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public required string Sku { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class Tag
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public List<Order> Orders { get; set; } = [];
}
