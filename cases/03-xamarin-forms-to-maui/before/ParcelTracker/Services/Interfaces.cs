using System.Collections.Generic;
using System.Threading.Tasks;
using ParcelTracker.Models;

namespace ParcelTracker.Services
{
    public interface IShipmentApi
    {
        Task<List<Shipment>> FindAsync(string status);
        Task<Shipment> GetAsync(string trackingNumber);
        Task<Shipment> CreateAsync(NewShipment shipment, string clientId);
        Task<Shipment> UpdateStatusAsync(string trackingNumber, string status);
        Task<decimal> QuoteAsync(decimal weightKg, string countryCode, string service);
    }

    /// <summary>Implemented once per platform and resolved through DependencyService.</summary>
    public interface IConnectivityService
    {
        bool IsOnline { get; }
    }

    /// <summary>Implemented once per platform: SharedPreferences on Android, NSUserDefaults on iOS.</summary>
    public interface ISettingsStore
    {
        string Get(string key, string defaultValue);
        void Set(string key, string value);
    }
}
