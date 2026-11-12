using System.Collections.Concurrent;

namespace Shipping.Core;

/// <summary>An instance, registered once per application, instead of a static class behind a lock.</summary>
public class ShipmentStore
{
    private readonly ConcurrentDictionary<string, Shipment> _items = new();
    private int _sequence;

    public ShipmentStore()
    {
        Add(n => new Shipment { TrackingNumber = n, RecipientName = "Anna Schmidt", City = "Berlin", PostalCode = "10115", CountryCode = "DE", WeightKg = 0.8m,
            Service = ServiceLevel.Standard, Status = ShipmentStatus.Created, Price = 4.90m, CreatedBy = "seed",
            CreatedUtc = new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc) });
        Add(n => new Shipment { TrackingNumber = n, RecipientName = "Jean Martin", City = "Lyon", PostalCode = "69001", CountryCode = "FR", WeightKg = 3.2m,
            Service = ServiceLevel.Express, Status = ShipmentStatus.InTransit, Price = 20.25m, CreatedBy = "seed",
            CreatedUtc = new DateTime(2026, 2, 2, 9, 30, 0, DateTimeKind.Utc) });
        Add(n => new Shipment { TrackingNumber = n, RecipientName = "John Miller", City = "Austin", PostalCode = "73301", CountryCode = "US", WeightKg = 12m,
            Service = ServiceLevel.Standard, Status = ShipmentStatus.Delivered, Price = 33.10m, CreatedBy = "seed",
            CreatedUtc = new DateTime(2026, 2, 3, 10, 0, 0, DateTimeKind.Utc), DeliveredUtc = new DateTime(2026, 2, 7, 15, 45, 0, DateTimeKind.Utc) });
    }

    public Shipment Add(Func<string, Shipment> create)
    {
        var shipment = create($"SHP-{Interlocked.Increment(ref _sequence):000000}");
        _items[shipment.TrackingNumber] = shipment;
        return shipment;
    }

    public Shipment? Find(string? trackingNumber) =>
        trackingNumber is not null && _items.TryGetValue(trackingNumber, out var shipment) ? shipment : null;

    public IReadOnlyList<Shipment> All() => [.. _items.Values.OrderBy(s => s.TrackingNumber, StringComparer.Ordinal)];
}
