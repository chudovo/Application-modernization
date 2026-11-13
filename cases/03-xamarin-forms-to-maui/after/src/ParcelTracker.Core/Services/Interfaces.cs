using ParcelTracker.Models;

namespace ParcelTracker.Services;

public interface IShipmentApi
{
    Task<List<Shipment>> FindAsync(string? status);
    Task<Shipment> GetAsync(string trackingNumber);
    Task<Shipment> CreateAsync(NewShipment shipment, string? clientId);
    Task<Shipment> UpdateStatusAsync(string trackingNumber, string status);
    Task<decimal> QuoteAsync(decimal weightKg, string? countryCode, string service);
}

// What used to be reached through DependencyService and Application.Current is now a set of small interfaces.
// The app project implements them with .NET MAUI; tests implement them with fakes.

public interface INetworkStatus
{
    bool IsOnline { get; }
}

public interface ISettings
{
    string? Get(string key);
    void Set(string key, string value);
}

public interface INavigator
{
    Task ShowDetailAsync(string trackingNumber, bool replaceCurrent = false);
    Task ShowNewShipmentAsync();
}

public interface IDialogs
{
    Task AlertAsync(string message);
}

/// <summary>Replaces the MessagingCenter string message "ShipmentChanged".</summary>
public record ShipmentChangedMessage;
