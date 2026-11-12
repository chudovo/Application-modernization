using System;
using System.Collections.Generic;
using System.Linq;
using OrderDesk.Domain;

namespace OrderDesk.Data
{
    public class NewOrderLine
    {
        public string Sku { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }

    /// <summary>
    /// Business operations. Synchronous throughout, and it creates its own context per call:
    /// the context lifetime is hidden inside the service instead of being owned by the request.
    /// </summary>
    public class OrderService
    {
        private readonly Func<OrderDeskContext> _contextFactory;
        private readonly PricingService _pricing = new PricingService();

        public OrderService(Func<OrderDeskContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public Order Get(int id)
        {
            using (var db = _contextFactory())
            {
                var order = db.Orders.Find(id);
                if (order == null) return null;
                Touch(order);
                return order;
            }
        }

        public List<Order> Find(int? customerId, OrderStatus? status)
        {
            using (var db = _contextFactory())
            {
                IQueryable<Order> query = db.Orders;
                if (customerId.HasValue) query = query.Where(o => o.CustomerId == customerId.Value);
                if (status.HasValue) query = query.Where(o => o.Status == status.Value);
                var orders = query.OrderBy(o => o.Id).ToList();
                foreach (var order in orders) Touch(order);
                return orders;
            }
        }

        public Order Create(int customerId, IList<NewOrderLine> lines)
        {
            if (lines == null || lines.Count == 0)
                throw new OrderValidationException("Order must contain at least one line.");
            if (lines.Any(l => l.Quantity <= 0 || l.UnitPrice < 0 || string.IsNullOrWhiteSpace(l.Sku)))
                throw new OrderValidationException("Every line needs a SKU, a positive quantity and a non-negative price.");

            using (var db = _contextFactory())
            {
                var customer = db.Customers.Find(customerId);
                if (customer == null)
                    throw new OrderValidationException("Customer " + customerId + " does not exist.");

                var order = new Order { CustomerId = customer.Id, Status = OrderStatus.New, CreatedUtc = Clock.UtcNow() };
                foreach (var line in lines)
                    order.Lines.Add(new OrderLine { Sku = line.Sku, Quantity = line.Quantity, UnitPrice = line.UnitPrice });

                _pricing.Price(order, customer);
                db.Orders.Add(order);
                db.SaveChanges();
                Touch(order);
                return order;
            }
        }

        public Order Cancel(int id)
        {
            using (var db = _contextFactory())
            {
                var order = db.Orders.Find(id);
                if (order == null) return null;
                if (order.Status == OrderStatus.Shipped)
                    throw new OrderConflictException("Order " + id + " is already shipped and cannot be cancelled.");

                order.Status = OrderStatus.Cancelled;
                db.SaveChanges();
                Touch(order);
                return order;
            }
        }

        // Forces the lazy-loaded navigation properties to load before the context is disposed.
        // Forget this call in one place and the API throws ObjectDisposedException during serialization.
        private static void Touch(Order order)
        {
            var name = order.Customer.Name;
            var count = order.Lines.Count;
        }
    }
}
