namespace ParcelTracker.Models;

public class Shipment
{
    public string TrackingNumber { get; set; } = "";
    public string RecipientName { get; set; } = "";
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "";
    public decimal WeightKg { get; set; }
    public string Service { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal Price { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime? DeliveredUtc { get; set; }

    public string Title => $"{TrackingNumber} · {RecipientName}";
    public string Subtitle => $"{City}, {CountryCode} · {StatusText.For(Status)}";
}

public record NewShipment(string? RecipientName, string? City, string? PostalCode, string? CountryCode, decimal WeightKg, string Service);

public static class StatusText
{
    public static string For(string status) => status == "InTransit" ? "In transit" : status;
}

public class ApiException(string code, string message, IReadOnlyList<string>? errors) : Exception(message)
{
    public string Code { get; } = code;
    public IReadOnlyList<string> Errors { get; } = errors ?? [];
}
