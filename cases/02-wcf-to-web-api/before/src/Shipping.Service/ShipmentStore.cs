using System;
using System.Collections.Generic;
using System.Linq;
using Shipping.Contracts;

namespace Shipping.Service
{
    /// <summary>Ambient clock. Tests overwrite the static delegate.</summary>
    public static class SystemClock
    {
        public static Func<DateTime> UtcNow = () => DateTime.UtcNow;
    }

    /// <summary>Process-wide state behind a lock. Every caller reaches it through the static members.</summary>
    public static class ShipmentStore
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, ShipmentData> Items = new Dictionary<string, ShipmentData>();
        private static int _sequence;

        static ShipmentStore()
        {
            Reset();
        }

        public static void Reset()
        {
            lock (Sync)
            {
                Items.Clear();
                _sequence = 0;
                Add(new ShipmentData { RecipientName = "Anna Schmidt", City = "Berlin", PostalCode = "10115", CountryCode = "DE", WeightKg = 0.8m,
                    Service = ServiceLevel.Standard, Status = ShipmentStatus.Created, Price = 4.90m, CreatedBy = "seed",
                    CreatedUtc = new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc) });
                Add(new ShipmentData { RecipientName = "Jean Martin", City = "Lyon", PostalCode = "69001", CountryCode = "FR", WeightKg = 3.2m,
                    Service = ServiceLevel.Express, Status = ShipmentStatus.InTransit, Price = 20.25m, CreatedBy = "seed",
                    CreatedUtc = new DateTime(2026, 2, 2, 9, 30, 0, DateTimeKind.Utc) });
                Add(new ShipmentData { RecipientName = "John Miller", City = "Austin", PostalCode = "73301", CountryCode = "US", WeightKg = 12m,
                    Service = ServiceLevel.Standard, Status = ShipmentStatus.Delivered, Price = 33.10m, CreatedBy = "seed",
                    CreatedUtc = new DateTime(2026, 2, 3, 10, 0, 0, DateTimeKind.Utc), DeliveredUtc = new DateTime(2026, 2, 7, 15, 45, 0, DateTimeKind.Utc) });
            }
        }

        public static ShipmentData Add(ShipmentData shipment)
        {
            lock (Sync)
            {
                shipment.TrackingNumber = "SHP-" + (++_sequence).ToString("000000");
                Items[shipment.TrackingNumber] = shipment;
                return shipment;
            }
        }

        public static ShipmentData Find(string trackingNumber)
        {
            lock (Sync)
            {
                ShipmentData shipment;
                return trackingNumber != null && Items.TryGetValue(trackingNumber, out shipment) ? shipment : null;
            }
        }

        public static List<ShipmentData> All()
        {
            lock (Sync) return Items.Values.OrderBy(s => s.TrackingNumber).ToList();
        }
    }
}
