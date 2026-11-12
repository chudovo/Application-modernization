namespace Shipping.Core;

/// <summary>
/// The business rules that used to live inside the WCF service class. It knows nothing about SOAP, HTTP or JSON:
/// errors are plain exceptions and the caller identity is a parameter.
/// </summary>
public class ShipmentManager(ShipmentStore store, TimeProvider clock)
{
    public Shipment Get(string? trackingNumber) =>
        store.Find(trackingNumber) ?? throw new ShipmentNotFoundException(trackingNumber);

    public IReadOnlyList<Shipment> Find(ShipmentStatus? status, string? countryCode)
    {
        IEnumerable<Shipment> result = store.All();
        if (status.HasValue) result = result.Where(s => s.Status == status.Value);
        if (!string.IsNullOrEmpty(countryCode)) result = result.Where(s => string.Equals(s.CountryCode, countryCode, StringComparison.OrdinalIgnoreCase));
        return [.. result];
    }

    public Shipment Create(NewShipment? request, string? callerId)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request?.RecipientName)) errors.Add(Rules.RecipientError);
        if (!Rules.IsCountryCode(request?.CountryCode)) errors.Add(Rules.CountryCodeError);
        if (request is null || !Rules.IsWeight(request.WeightKg)) errors.Add(Rules.WeightError);
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        return store.Add(trackingNumber => new Shipment
        {
            TrackingNumber = trackingNumber,
            RecipientName = request!.RecipientName!.Trim(),
            City = request.City,
            PostalCode = request.PostalCode,
            CountryCode = request.CountryCode!.ToUpperInvariant(),
            WeightKg = request.WeightKg,
            Service = request.Service,
            Status = ShipmentStatus.Created,
            Price = RateCalculator.Rate(request.WeightKg, request.CountryCode, request.Service),
            CreatedBy = string.IsNullOrEmpty(callerId) ? "unknown" : callerId,
            CreatedUtc = clock.GetUtcNow().UtcDateTime
        });
    }

    public Shipment UpdateStatus(string? trackingNumber, ShipmentStatus newStatus)
    {
        var shipment = Get(trackingNumber);
        lock (shipment)
        {
            var allowed = (shipment.Status, newStatus) is
                (ShipmentStatus.Created, ShipmentStatus.InTransit) or
                (ShipmentStatus.InTransit, ShipmentStatus.Delivered) or
                (ShipmentStatus.InTransit, ShipmentStatus.Returned);
            if (!allowed) throw new InvalidTransitionException(shipment.Status, newStatus);

            shipment.Status = newStatus;
            if (newStatus == ShipmentStatus.Delivered) shipment.DeliveredUtc = clock.GetUtcNow().UtcDateTime;
            return shipment;
        }
    }
}
