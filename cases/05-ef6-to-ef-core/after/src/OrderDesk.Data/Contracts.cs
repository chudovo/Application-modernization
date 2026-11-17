#nullable disable
namespace OrderDesk.Data
{
    // What the rest of the application sees. These types are identical in the Entity Framework 6 and the EF Core
    // version, which is what lets one file of scenarios compile against both.

    public class NewLine
    {
        public NewLine(string sku, int quantity, decimal unitPrice) { Sku = sku; Quantity = quantity; UnitPrice = unitPrice; }
        public string Sku { get; }
        public int Quantity { get; }
        public decimal UnitPrice { get; }
    }

    public class NewOrder
    {
        public NewOrder(int customerId, DateTime createdUtc, IReadOnlyList<NewLine> lines, params string[] tags)
        { CustomerId = customerId; CreatedUtc = createdUtc; Lines = lines; Tags = tags; }
        public int CustomerId { get; }
        public DateTime CreatedUtc { get; }
        public IReadOnlyList<NewLine> Lines { get; }
        public IReadOnlyList<string> Tags { get; }
    }

    public class OrderDetails
    {
        public int Id { get; set; }
        public string CustomerName { get; set; }
        public string City { get; set; }
        public DateTime CreatedUtc { get; set; }
        public OrderStatus Status { get; set; }
        public decimal Total { get; set; }
        public byte[] RowVersion { get; set; }
        public IReadOnlyList<LineDetails> Lines { get; set; }
        public IReadOnlyList<string> Tags { get; set; }
    }

    public class LineDetails
    {
        public string Sku { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }

    public class CustomerDetails
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Street { get; set; }
        public string City { get; set; }
        public string PostalCode { get; set; }
        public string CountryCode { get; set; }
        public int OrderCount { get; set; }
    }

    public class CustomerRevenue
    {
        public string CustomerName { get; set; }
        public int Orders { get; set; }
        public decimal Revenue { get; set; }
    }

    public class OrderSummary
    {
        public int Id { get; set; }
        public string CustomerName { get; set; }
        public DateTime CreatedUtc { get; set; }
        public decimal Total { get; set; }
        public int LineCount { get; set; }
    }

    /// <summary>Someone else changed the order after it was read.</summary>
    public class ConcurrencyConflictException : Exception
    {
        public ConcurrencyConflictException(int orderId, Exception inner) : base("Order " + orderId + " was changed by someone else.", inner) { }
    }

    public class NotFoundException : Exception
    {
        public NotFoundException(string what) : base(what + " was not found.") { }
    }
}
