using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;
using ParcelTracker.App.Controls;
using ParcelTracker.App.Services;
using ParcelTracker.App.Views;
using ParcelTracker.Services;
using ParcelTracker.ViewModels;

namespace ParcelTracker.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Replaces DependencyService.Register and the [assembly: Dependency] attributes of the platform projects.
        builder.Services.AddHttpClient<IShipmentApi, ShipmentApi>(http => http.BaseAddress = new Uri(AppSettings.ApiBaseUrl));
        builder.Services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        builder.Services.AddSingleton<INetworkStatus, MauiNetworkStatus>();
        builder.Services.AddSingleton<ISettings, PreferencesSettings>();
        builder.Services.AddSingleton<INavigator, ShellNavigator>();
        builder.Services.AddSingleton<IDialogs, ShellDialogs>();

        builder.Services.AddTransient<ShipmentListViewModel>().AddTransient<ShipmentListPage>();
        builder.Services.AddTransient<ShipmentDetailViewModel>().AddTransient<ShipmentDetailPage>();
        builder.Services.AddTransient<NewShipmentViewModel>().AddTransient<NewShipmentPage>();

        // Replaces the two custom renderers (one per platform project) for BorderlessEntry.
        EntryHandler.Mapper.AppendToMapping("Borderless", (handler, view) =>
        {
            if (view is not BorderlessEntry) return;
#if ANDROID
            handler.PlatformView.Background = null;
#elif IOS || MACCATALYST
            handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#elif WINDOWS
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
#endif
        });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
