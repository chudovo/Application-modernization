using System.Windows.Input;
using Microsoft.AspNetCore.Builder;
using ParcelTracker.Services;
using ParcelTracker.ViewModels;
using ParcelTracker.Views;
using Xamarin.Forms;
using Xamarin.Forms.Internals;
using Xamarin.Forms.Mocks;
using Xunit;

// The app keeps its configuration and its services in statics, so scenarios cannot run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ParcelTracker.Scenarios;

/// <summary>
/// Drives the Xamarin.Forms version: the real App, its real pages and view models, on a mocked platform layer.
/// Connectivity and settings, which each platform project implements natively, are replaced through DependencyService.
/// </summary>
public sealed class AppHarness : IAsyncDisposable, IConnectivityService, ISettingsStore
{
    private readonly WebApplication _backend;
    private readonly Dictionary<string, string> _settings = [];
    private readonly UiThread _ui = new();

    private AppHarness(WebApplication backend, bool online, string? clientId)
    {
        _backend = backend;
        Online = online;
        if (clientId is not null) _settings["client.id"] = clientId;
        Backend = new HttpClient { BaseAddress = new Uri(backend.Urls.First()) };
        _ui.Invoke(Start);
    }

    private void Start()
    {
        MockForms.Init();
        AppConfig.ApiBaseUrl = _backend.Urls.First();
        DependencyService.RegisterSingleton<IConnectivityService>(this);
        DependencyService.RegisterSingleton<ISettingsStore>(this);
        MessagingCenter.Subscribe<Page, AlertArguments>(this, Page.AlertSignalName, (_, alert) =>
        {
            Alerts.Add(alert.Message);
            alert.SetResult(true);
        });

        Application.Current = new App();
        // Without a platform renderer the page counts as not yet on screen, and DisplayAlert would queue its alert forever.
        Application.Current.MainPage.IsPlatformEnabled = true;
    }

    public static async Task<AppHarness> StartAsync(bool online = true, string? clientId = "device-1") =>
        new(await Scenarios.Backend.StartAsync(), online, clientId);

    public HttpClient Backend { get; }
    public bool Online { get; set; }
    public List<string> Alerts { get; } = [];

    private static IReadOnlyList<Page> Pages => Application.Current.MainPage.Navigation.NavigationStack;

    public ShipmentListViewModel List => (ShipmentListViewModel)Pages[0].BindingContext;
    public ShipmentDetailViewModel Detail => (ShipmentDetailViewModel)Pages[^1].BindingContext;
    public NewShipmentViewModel Form => (NewShipmentViewModel)Pages[^1].BindingContext;
    public string CurrentScreen => Name(Pages[^1]);
    public IReadOnlyList<string> ScreenStack => [.. Pages.Select(Name)];

    private static string Name(Page page) => page switch
    {
        ShipmentListPage => "List",
        ShipmentDetailPage detail => $"Detail:{((ShipmentDetailViewModel)detail.BindingContext).Title}",
        NewShipmentPage => "New",
        _ => page.GetType().Name
    };

    /// <summary>Raises the Appearing event of the top page, which is where the pages load their data.</summary>
    public Task Appear() => Do(() => ((IPageController)Pages[^1]).SendAppearing());

    public Task Run(ICommand command, object? parameter = null) => Do(() => command.Execute(parameter));

    // Everything that touches the app runs on one dedicated thread, as it does on a device.
    public Task Do(Action action)
    {
        _ui.Invoke(action);
        return Idle();
    }

    // Commands are "async void" underneath and cannot be awaited, so wait until every screen is quiet.
    private static async Task Idle()
    {
        for (var quiet = 0; quiet < 3;)
        {
            await Task.Delay(15);
            quiet = Pages.Any(p => p.BindingContext is BaseViewModel { IsBusy: true }) ? 0 : quiet + 1;
        }
    }

    bool IConnectivityService.IsOnline => Online;
    string ISettingsStore.Get(string key, string defaultValue) => _settings.GetValueOrDefault(key, defaultValue);
    void ISettingsStore.Set(string key, string value) => _settings[key] = value;

    public async ValueTask DisposeAsync()
    {
        MessagingCenter.Unsubscribe<Page, AlertArguments>(this, Page.AlertSignalName);
        _ui.Dispose();
        Backend.Dispose();
        await _backend.DisposeAsync();
    }
}

/// <summary>
/// A stand-in for the device's UI thread: one thread with a SynchronizationContext, so that code after an await in a
/// view model resumes on the same thread and never runs in parallel with other UI code.
/// </summary>
public sealed class UiThread : SynchronizationContext, IDisposable
{
    private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];
    private readonly Thread _thread;

    public UiThread()
    {
        _thread = new Thread(() =>
        {
            SetSynchronizationContext(this);
            foreach (var (callback, state) in _queue.GetConsumingEnumerable()) callback(state);
        }) { IsBackground = true, Name = "ui" };
        _thread.Start();
    }

    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

    public void Invoke(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(_ =>
        {
            try { action(); done.SetResult(); }
            catch (Exception e) { done.SetException(e); }
        }, null);
        done.Task.GetAwaiter().GetResult();
    }

    public void Dispose() => _queue.CompleteAdding();
}
