using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ParcelTracker.Services;
using ParcelTracker.ViewModels;

namespace ParcelTracker.Scenarios;

/// <summary>
/// Drives the .NET MAUI version at view-model level. The view models come from the same dependency injection setup the
/// app uses; navigation, dialogs, connectivity and settings are replaced by in-memory fakes. No UI framework is loaded.
/// </summary>
public sealed class AppHarness : IAsyncDisposable, INavigator, IDialogs, INetworkStatus, ISettings
{
    private readonly WebApplication _backend;
    private readonly ServiceProvider _services;
    private readonly List<(string Name, object ViewModel)> _stack = [];
    private readonly Dictionary<string, string> _settings = [];

    private AppHarness(WebApplication backend, bool online, string? clientId)
    {
        _backend = backend;
        Online = online;
        if (clientId is not null) _settings["client.id"] = clientId;
        Backend = new HttpClient { BaseAddress = new Uri(backend.Urls.First()) };

        var services = new ServiceCollection();
        services.AddSingleton<IShipmentApi>(new ShipmentApi(new HttpClient { BaseAddress = new Uri(backend.Urls.First()) }));
        services.AddSingleton<IMessenger>(new WeakReferenceMessenger());
        services.AddSingleton<INavigator>(this).AddSingleton<IDialogs>(this).AddSingleton<INetworkStatus>(this).AddSingleton<ISettings>(this);
        services.AddTransient<ShipmentListViewModel>().AddTransient<ShipmentDetailViewModel>().AddTransient<NewShipmentViewModel>();
        _services = services.BuildServiceProvider();

        _stack.Add(("List", _services.GetRequiredService<ShipmentListViewModel>()));
    }

    public static async Task<AppHarness> StartAsync(bool online = true, string? clientId = "device-1") =>
        new(await Scenarios.Backend.StartAsync(), online, clientId);

    public HttpClient Backend { get; }
    public bool Online { get; set; }
    public List<string> Alerts { get; } = [];

    public ShipmentListViewModel List => (ShipmentListViewModel)_stack[0].ViewModel;
    public ShipmentDetailViewModel Detail => (ShipmentDetailViewModel)_stack[^1].ViewModel;
    public NewShipmentViewModel Form => (NewShipmentViewModel)_stack[^1].ViewModel;
    public string CurrentScreen => _stack[^1].Name;
    public IReadOnlyList<string> ScreenStack => [.. _stack.Select(s => s.Name)];

    /// <summary>What the page does when it appears on screen: load its data.</summary>
    public Task Appear() => _stack[^1].ViewModel switch
    {
        ShipmentListViewModel list => Run(list.LoadCommand),
        ShipmentDetailViewModel detail => Run(detail.LoadCommand),
        _ => Task.CompletedTask
    };

    public async Task Run(ICommand command, object? parameter = null)
    {
        if (command is IAsyncRelayCommand asyncCommand) await asyncCommand.ExecuteAsync(parameter);
        else command.Execute(parameter);
        await Idle();
    }

    public async Task Do(Action action)
    {
        action();
        await Idle();
    }

    // Reloads triggered by a message or a property change are not awaited by anyone, so wait until every screen is quiet.
    private async Task Idle()
    {
        for (var quiet = 0; quiet < 3;)
        {
            await Task.Delay(15);
            quiet = _stack.Any(s => s.ViewModel is ShipmentListViewModel { IsBusy: true } or ShipmentDetailViewModel { IsBusy: true } or NewShipmentViewModel { IsBusy: true }) ? 0 : quiet + 1;
        }
    }

    Task INavigator.ShowDetailAsync(string trackingNumber, bool replaceCurrent)
    {
        var detail = _services.GetRequiredService<ShipmentDetailViewModel>();
        detail.Initialize(trackingNumber);
        if (replaceCurrent) _stack.RemoveAt(_stack.Count - 1);
        _stack.Add(($"Detail:{trackingNumber}", detail));
        return Task.CompletedTask;
    }

    Task INavigator.ShowNewShipmentAsync()
    {
        _stack.Add(("New", _services.GetRequiredService<NewShipmentViewModel>()));
        return Task.CompletedTask;
    }

    Task IDialogs.AlertAsync(string message)
    {
        Alerts.Add(message);
        return Task.CompletedTask;
    }

    bool INetworkStatus.IsOnline => Online;
    string? ISettings.Get(string key) => _settings.GetValueOrDefault(key);
    void ISettings.Set(string key, string value) => _settings[key] = value;

    public async ValueTask DisposeAsync()
    {
        Backend.Dispose();
        await _services.DisposeAsync();
        await _backend.DisposeAsync();
    }
}
