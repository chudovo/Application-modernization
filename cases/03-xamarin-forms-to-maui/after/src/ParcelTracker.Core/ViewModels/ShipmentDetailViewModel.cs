using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ParcelTracker.Models;
using ParcelTracker.Services;

namespace ParcelTracker.ViewModels;

public partial class ShipmentDetailViewModel(IShipmentApi api, IDialogs dialogs, IMessenger messenger) : ObservableObject
{
    [ObservableProperty] public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel), nameof(DeliveredText), nameof(CanMarkInTransit), nameof(CanMarkDelivered))]
    public partial Shipment? Shipment { get; set; }

    [ObservableProperty] public partial string Title { get; set; } = "";

    public string StatusLabel => Shipment is null ? "" : StatusText.For(Shipment.Status);
    public bool CanMarkInTransit => Shipment?.Status == "Created";
    public bool CanMarkDelivered => Shipment?.Status == "InTransit";

    public string DeliveredText => Shipment?.DeliveredUtc is { } delivered
        ? $"Delivered {delivered.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC"
        : "";

    /// <summary>The tracking number arrives as a navigation parameter instead of a constructor argument.</summary>
    public void Initialize(string trackingNumber) => Title = trackingNumber;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Shipment = await api.GetAsync(Title);
        }
        catch (ApiException ex)
        {
            await dialogs.AlertAsync(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task MarkInTransitAsync() => ChangeStatusAsync("InTransit");

    [RelayCommand]
    private Task MarkDeliveredAsync() => ChangeStatusAsync("Delivered");

    private async Task ChangeStatusAsync(string status)
    {
        IsBusy = true;
        try
        {
            Shipment = await api.UpdateStatusAsync(Title, status);
            messenger.Send(new ShipmentChangedMessage());
        }
        catch (ApiException ex)
        {
            // Someone else changed the shipment in the meantime: tell the user and show the current state.
            await dialogs.AlertAsync(ex.Message);
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
