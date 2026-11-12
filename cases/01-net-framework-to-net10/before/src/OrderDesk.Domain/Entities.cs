using System;
using System.Collections.Generic;

namespace OrderDesk.Domain
{
    public enum OrderStatus { New = 0, Paid = 1, Shipped = 2, Cancelled = 3 }

    public enum CustomerTier { Standard = 0, Gold = 1 }

    public class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public CustomerTier Tier { get; set; }
        public bool VatExempt { get; set; }
    }

    public class Order
    {
        public Order()
        {
            Lines = new List<OrderLine>();
        }

        public int Id { get; set; }
        public int CustomerId { get; set; }

        // Navigation properties are virtual because the EF6 context relies on lazy loading.
        public virtual Customer Customer { get; set; }
        public virtual ICollection<OrderLine> Lines { get; set; }

        public OrderStatus Status { get; set; }
        public DateTime CreatedUtc { get; set; }
        public decimal Subtotal { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal NetTotal { get; set; }
        public decimal VatTotal { get; set; }
        public decimal GrossTotal { get; set; }
    }

    public class OrderLine
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public string Sku { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }
}
