using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ParcelTracker.Models;
using ParcelTracker.Services;

namespace ParcelTracker.ViewModels;

public partial class ShipmentListViewModel : ObservableObject, IRecipient<ShipmentChangedMessage>
{
    private const string CacheKey = "shipments.cache";

    private readonly IShipmentApi _api;
    private readonly INetworkStatus _connectivity;
    private readonly ISettings _settings;
    private readonly INavigator _navigator;

    public ShipmentListViewModel(IShipmentApi api, INetworkStatus connectivity, ISettings settings, INavigator navigator, IMessenger messenger)
    {
        (_api, _connectivity, _settings, _navigator) = (api, connectivity, settings, navigator);
        // A weak, typed subscription: no string keys, and no leak if the view model is dropped.
        messenger.Register(this);
    }

    public ObservableCollection<Shipment> Items { get; } = [];
    public IReadOnlyList<string> StatusFilters { get; } = ["All", "Created", "InTransit", "Delivered", "Returned"];

    [ObservableProperty] public partial bool IsBusy { get; set; }

    // Bound to the pull-to-refresh control. It is only ever set back to false here: setting it to true from code would
    // make the control run the load command again.
    [ObservableProperty] public partial bool IsRefreshing { get; set; }
    [ObservableProperty] public partial bool IsOffline { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }
    [ObservableProperty] public partial string StatusFilter { get; set; } = "All";

    partial void OnStatusFilterChanged(string value) => LoadCommand.Execute(null);

    public void Receive(ShipmentChangedMessage message) => LoadCommand.Execute(null);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            ErrorMessage = null;
            IsOffline = !_connectivity.IsOnline;

            List<Shipment> shipments;
            if (IsOffline)
            {
                var cached = _settings.Get(CacheKey);
                shipments = cached is null ? [] : JsonSerializer.Deserialize<List<Shipment>>(cached)!;
                if (cached is null) ErrorMessage = "You are offline. No saved shipments yet.";
                if (StatusFilter != "All") shipments = [.. shipments.Where(s => s.Status == StatusFilter)];
            }
            else
            {
                shipments = await _api.FindAsync(StatusFilter == "All" ? null : StatusFilter);
                if (StatusFilter == "All") _settings.Set(CacheKey, JsonSerializer.Serialize(shipments));
            }

            // No explicit switch to the UI thread: the await above resumes on the context it started on.
            Items.Clear();
            foreach (var shipment in shipments) Items.Add(shipment);
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private Task OpenAsync(Shipment shipment) => _navigator.ShowDetailAsync(shipment.TrackingNumber);

    [RelayCommand]
    private Task NewAsync() => _navigator.ShowNewShipmentAsync();
}
