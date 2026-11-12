namespace Shipping.Core;

public enum ShipmentStatus { Created, InTransit, Delivered, Returned }

public enum ServiceLevel { Standard, Express }

public class Shipment
{
    public required string TrackingNumber { get; init; }
    public required string RecipientName { get; init; }
    public string? City { get; init; }
    public string? PostalCode { get; init; }
    public required string CountryCode { get; init; }
    public decimal WeightKg { get; init; }
    public ServiceLevel Service { get; init; }
    public ShipmentStatus Status { get; set; }
    public decimal Price { get; init; }
    public required string CreatedBy { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime? DeliveredUtc { get; set; }
}

public record NewShipment(string? RecipientName, string? City, string? PostalCode, string? CountryCode, decimal WeightKg, ServiceLevel Service);

public class ShipmentNotFoundException(string? trackingNumber) : Exception($"Shipment {trackingNumber} was not found.")
{
    public string? TrackingNumber { get; } = trackingNumber;
}

public class ValidationFailedException(IReadOnlyList<string> errors) : Exception("The request is not valid.")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public class InvalidTransitionException(ShipmentStatus from, ShipmentStatus to) : Exception($"Cannot change status from {from} to {to}.")
{
    public ShipmentStatus From { get; } = from;
    public ShipmentStatus To { get; } = to;
}
