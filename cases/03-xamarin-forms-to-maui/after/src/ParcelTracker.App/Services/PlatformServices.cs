using ParcelTracker.Services;

namespace ParcelTracker.App.Services;

/// <summary>One implementation for all platforms, instead of ConnectivityManager on Android and NetworkReachability on iOS.</summary>
public class MauiNetworkStatus(Microsoft.Maui.Networking.IConnectivity connectivity) : INetworkStatus
{
    public bool IsOnline => connectivity.NetworkAccess == NetworkAccess.Internet;
}

/// <summary>
/// One implementation instead of SharedPreferences on Android and NSUserDefaults on iOS. It has to keep reading what the
/// Xamarin app stored on the device, otherwise the device identity and the offline cache are lost on update.
/// </summary>
public class PreferencesSettings : ISettings
{
#if ANDROID
    // The Xamarin app used a SharedPreferences file with this name. Without it, Preferences would use its own file.
    private const string SharedName = "parceltracker";
#else
    // On iOS the Xamarin app used the standard user defaults, which is also what Preferences uses by default.
    private const string? SharedName = null;
#endif

    public string? Get(string key) => Preferences.Default.Get<string?>(key, null, SharedName);

    public void Set(string key, string value) => Preferences.Default.Set(key, value, SharedName);
}

/// <summary>Navigation through Shell routes. View models no longer create pages.</summary>
public class ShellNavigator : INavigator
{
    public Task ShowDetailAsync(string trackingNumber, bool replaceCurrent = false) =>
        Shell.Current.GoToAsync($"{(replaceCurrent ? "../" : "")}detail?trackingNumber={Uri.EscapeDataString(trackingNumber)}");

    public Task ShowNewShipmentAsync() => Shell.Current.GoToAsync("new");
}

public class ShellDialogs : IDialogs
{
    public Task AlertAsync(string message) => Shell.Current.DisplayAlertAsync("Parcel Tracker", message, "OK");
}
