using System;
using System.Collections.Generic;
using System.Linq;
using OrderDesk.Domain;

namespace OrderDesk.Api
{
    public class CreateOrderRequest
    {
        public int CustomerId { get; set; }
        public List<CreateOrderLine> Lines { get; set; }
    }

    public class CreateOrderLine
    {
        public string Sku { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }

    public class OrderDto
    {
        public int Id { get; set; }
        public string CustomerName { get; set; }
        public OrderStatus Status { get; set; }
        public DateTime CreatedUtc { get; set; }
        public List<OrderLineDto> Lines { get; set; }
        public decimal Subtotal { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal NetTotal { get; set; }
        public decimal VatTotal { get; set; }
        public decimal GrossTotal { get; set; }

        public static OrderDto From(Order order)
        {
            return new OrderDto
            {
                Id = order.Id,
                CustomerName = order.Customer.Name,
                Status = order.Status,
                // SQL Server returns DateTime with Kind=Unspecified; without this the "Z" suffix disappears from the JSON.
                CreatedUtc = DateTime.SpecifyKind(order.CreatedUtc, DateTimeKind.Utc),
                Lines = order.Lines.OrderBy(l => l.Id).Select(l => new OrderLineDto
                {
                    Sku = l.Sku, Quantity = l.Quantity, UnitPrice = l.UnitPrice, LineTotal = l.Quantity * l.UnitPrice
                }).ToList(),
                Subtotal = order.Subtotal,
                DiscountPercent = order.DiscountPercent,
                NetTotal = order.NetTotal,
                VatTotal = order.VatTotal,
                GrossTotal = order.GrossTotal
            };
        }
    }

    public class OrderLineDto
    {
        public string Sku { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }
    }
}
