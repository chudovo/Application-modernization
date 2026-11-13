// Shared by both test projects. The same source file is compiled once against the Xamarin.Forms view models and once
// against the .NET MAUI view models. Everything version-specific is behind AppHarness, which each project implements.
using System.Globalization;
using System.Net.Http.Json;
using Xunit;

namespace ParcelTracker.Scenarios;

public class ShipmentListScenarios
{
    [Fact]
    public async Task List_shows_all_shipments_in_tracking_number_order()
    {
        await using var app = await AppHarness.StartAsync();
        await app.Appear();

        Assert.Equal(["SHP-000001", "SHP-000002", "SHP-000003"], app.List.Items.Select(s => s.TrackingNumber));
        Assert.Equal("SHP-000002 · Jean Martin", app.List.Items[1].Title);
        Assert.Equal("Lyon, FR · In transit", app.List.Items[1].Subtitle);
        Assert.False(app.List.IsOffline);
    }

    [Fact]
    public async Task Choosing_a_status_filter_reloads_the_list()
    {
        await using var app = await AppHarness.StartAsync();
        await app.Appear();

        await app.Do(() => app.List.StatusFilter = "InTransit");

        Assert.Equal(["SHP-000002"], app.List.Items.Select(s => s.TrackingNumber));
    }

    [Fact]
    public async Task Offline_without_saved_data_shows_a_message_and_an_empty_list()
    {
        await using var app = await AppHarness.StartAsync(online: false);
        await app.Appear();

        Assert.True(app.List.IsOffline);
        Assert.Empty(app.List.Items);
        Assert.Equal("You are offline. No saved shipments yet.", app.List.ErrorMessage);
    }

    [Fact]
    public async Task Offline_after_a_successful_load_shows_the_saved_shipments()
    {
        await using var app = await AppHarness.StartAsync();
        await app.Appear();

        app.Online = false;
        await app.Run(app.List.LoadCommand);

        Assert.True(app.List.IsOffline);
        Assert.Equal(3, app.List.Items.Count);
        Assert.Null(app.List.ErrorMessage);
    }

    [Fact]
    public async Task Tapping_a_shipment_opens_its_detail_screen()
    {
        await using var app = await AppHarness.StartAsync();
        await app.Appear();

        await app.Run(app.List.OpenCommand, app.List.Items[1]);
        await app.Appear();

        Assert.Equal("Detail:SHP-000002", app.CurrentScreen);
        Assert.Equal("In transit", app.Detail.StatusLabel);
        Assert.True(app.Detail.CanMarkDelivered);
        Assert.False(app.Detail.CanMarkInTransit);
    }
}

public class ShipmentDetailScenarios
{
    [Fact]
    public async Task Marking_as_delivered_updates_the_detail_and_the_list_behind_it()
    {
        await using var app = await AppHarness.StartAsync();
        await app.Appear();
        await app.Run(app.List.OpenCommand, app.List.Items[1]);
        await app.Appear();

        await app.Run(app.Detail.MarkDeliveredCommand);

        Assert.Equal("Delivered", app.Detail.StatusLabel);
        Assert.Equal("Delivered 2026-02-10 12:00 UTC", app.Detail.DeliveredText);
        Assert.False(app.Detail.CanMarkDelivered);
        Assert.Equal("Delivered", app.List.Items[1].Status);
    }

    [Fact]
    public async Task A_delivered_shipment_offers_no_actions()
    {
        await using var app = await AppHarness.StartAsync();
        await app.Appear();
        await app.Run(app.List.OpenCommand, app.List.Items[2]);
        await app.Appear();

        Assert.Equal("Delivered 2026-02-07 15:45 UTC", app.Detail.DeliveredText);
        Assert.False(app.Detail.CanMarkInTransit);
        Assert.False(app.Detail.CanMarkDelivered);
    }

    [Fact]
    public async Task A_change_made_elsewhere_is_reported_and_the_screen_shows_the_current_state()
    {
        await using var app = await AppHarness.StartAsync();
        await app.Appear();
        await app.Run(app.List.OpenCommand, app.List.Items[0]);
        await app.Appear();
        Assert.True(app.Detail.CanMarkInTransit);

        // Another user moves the same shipment through to Delivered while this screen is open.
        foreach (var status in new[] { "InTransit", "Delivered" })
            (await app.Backend.PutAsJsonAsync("/api/shipments/SHP-000001/status", new { status })).EnsureSuccessStatusCode();

        await app.Run(app.Detail.MarkInTransitCommand);

        Assert.Equal(["Cannot change status from Delivered to InTransit."], app.Alerts);
        Assert.Equal("Delivered", app.Detail.StatusLabel);
    }
}

public class NewShipmentScenarios
{
    [Fact]
    public async Task Invalid_input_is_rejected_on_the_device_with_the_same_messages_the_service_uses()
    {
        await using var app = await AppHarness.StartAsync();
        await app.Appear();
        await app.Run(app.List.NewCommand);

        app.Form.RecipientName = "";
        app.Form.CountryCode = "ITA";
        app.Form.WeightText = "45";
        await app.Run(app.Form.SaveCommand);

        Assert.Equal(
            ["RecipientName is required.", "CountryCode must be a two-letter ISO code.", "WeightKg must be greater than 0 and at most 30."],
            app.Form.Errors);
        Assert.Equal("New", app.CurrentScreen);
        Assert.Equal(3, (await app.Backend.GetFromJsonAsync<List<object>>("/api/shipments"))!.Count);
    }

    [Fact]
    public async Task A_weight_typed_with_a_decimal_comma_is_understood_on_a_German_device()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            await using var app = await AppHarness.StartAsync();
            await app.Appear();
            await app.Run(app.List.NewCommand);

            app.Form.RecipientName = "Maria Rossi";
            app.Form.CountryCode = "IT";
            app.Form.WeightText = "2,5";
            await app.Run(app.Form.QuoteCommand);

            Assert.Empty(app.Form.Errors);
            Assert.Equal("12.30 EUR", app.Form.QuoteText);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task Saving_creates_the_shipment_under_the_device_identity_and_opens_its_detail()
    {
        await using var app = await AppHarness.StartAsync(clientId: "device-42");
        await app.Appear();
        await app.Run(app.List.NewCommand);

        app.Form.RecipientName = "Maria Rossi";
        app.Form.City = "Milano";
        app.Form.PostalCode = "20121";
        app.Form.CountryCode = "IT";
        app.Form.WeightText = "2";
        app.Form.IsExpress = true;
        await app.Run(app.Form.SaveCommand);
        await app.Appear();

        Assert.Equal("Detail:SHP-000004", app.CurrentScreen);
        Assert.Equal("Created", app.Detail.StatusLabel);
        Assert.Equal("device-42", app.Detail.Shipment!.CreatedBy);
        Assert.Equal(16.65m, app.Detail.Shipment.Price);
        Assert.Equal(4, app.List.Items.Count);
        Assert.Equal(["List", "Detail:SHP-000004"], app.ScreenStack);
    }
}
