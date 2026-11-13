using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ParcelTracker.Models;
using ParcelTracker.Services;

namespace ParcelTracker.ViewModels;

public partial class NewShipmentViewModel(IShipmentApi api, ISettings settings, INavigator navigator, IMessenger messenger) : ObservableObject
{
    public string? RecipientName { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
    public string? WeightText { get; set; }
    public bool IsExpress { get; set; }

    public ObservableCollection<string> Errors { get; } = [];

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string QuoteText { get; set; } = "";

    private string Service => IsExpress ? "Express" : "Standard";

    // The weight is typed by the user, so it is parsed with the device culture: "2,5" is valid on a German phone.
    private decimal? Weight => decimal.TryParse(WeightText, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) ? value : null;

    private bool Validate()
    {
        Errors.Clear();
        if (string.IsNullOrWhiteSpace(RecipientName)) Errors.Add("RecipientName is required.");
        if (CountryCode is not { Length: 2 } || !CountryCode.All(char.IsLetter)) Errors.Add("CountryCode must be a two-letter ISO code.");
        if (Weight is not (> 0 and <= 30)) Errors.Add("WeightKg must be greater than 0 and at most 30.");
        return Errors.Count == 0;
    }

    [RelayCommand]
    private async Task QuoteAsync()
    {
        QuoteText = "";
        if (!Validate()) return;
        IsBusy = true;
        try
        {
            var price = await api.QuoteAsync(Weight!.Value, CountryCode, Service);
            QuoteText = $"{price.ToString("0.00", CultureInfo.InvariantCulture)} EUR";
        }
        catch (ApiException ex)
        {
            foreach (var error in ex.Errors.DefaultIfEmpty(ex.Message)) Errors.Add(error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!Validate()) return;
        IsBusy = true;
        try
        {
            var created = await api.CreateAsync(new NewShipment(RecipientName, City, PostalCode, CountryCode, Weight!.Value, Service), settings.Get("client.id"));
            messenger.Send(new ShipmentChangedMessage());
            // Replace the form with the detail page of the new shipment.
            await navigator.ShowDetailAsync(created.TrackingNumber, replaceCurrent: true);
        }
        catch (ApiException ex)
        {
            foreach (var error in ex.Errors.DefaultIfEmpty(ex.Message)) Errors.Add(error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
