using System;
using OrderDesk.Domain;

namespace OrderDesk.Data
{
    public static class SeedData
    {
        public static void Apply(OrderDeskContext db)
        {
            var northwind = db.Customers.Add(new Customer { Name = "Northwind Traders", Tier = CustomerTier.Standard });
            db.SaveChanges();
            var alpine = db.Customers.Add(new Customer { Name = "Alpine Tools GmbH", Tier = CustomerTier.Gold });
            db.SaveChanges();
            db.Customers.Add(new Customer { Name = "Harbor Clinic", Tier = CustomerTier.Standard, VatExempt = true });
            db.SaveChanges();

            var pricing = new PricingService();

            // One SaveChanges per order keeps the identity values deterministic.
            AddOrder(db, pricing, northwind, OrderStatus.New, new DateTime(2026, 1, 10, 9, 30, 0, DateTimeKind.Utc),
                new OrderLine { Sku = "SKU-100", Quantity = 2, UnitPrice = 49.90m },
                new OrderLine { Sku = "SKU-200", Quantity = 1, UnitPrice = 120.00m });
            AddOrder(db, pricing, alpine, OrderStatus.Paid, new DateTime(2026, 1, 11, 14, 0, 0, DateTimeKind.Utc),
                new OrderLine { Sku = "SKU-300", Quantity = 10, UnitPrice = 150.00m });
            AddOrder(db, pricing, northwind, OrderStatus.Shipped, new DateTime(2026, 1, 12, 8, 15, 0, DateTimeKind.Utc),
                new OrderLine { Sku = "SKU-100", Quantity = 1, UnitPrice = 49.90m });
        }

        private static void AddOrder(OrderDeskContext db, PricingService pricing, Customer customer, OrderStatus status, DateTime createdUtc, params OrderLine[] lines)
        {
            var order = new Order { CustomerId = customer.Id, Status = status, CreatedUtc = createdUtc };
            foreach (var line in lines) order.Lines.Add(line);
            pricing.Price(order, customer);
            db.Orders.Add(order);
            db.SaveChanges();
        }
    }
}
