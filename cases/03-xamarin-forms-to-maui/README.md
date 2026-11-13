# Case 03: Xamarin.Forms to .NET MAUI

A mobile app for tracking shipments, written with Xamarin.Forms 5, and the same app on .NET MAUI with .NET 10.
The app is a client of the shipment service from [case 02](../02-wcf-to-web-api/).

The same 11 screen-level scenarios are compiled against the view models of both versions and run against the real
service over HTTP.

| | Before | After |
|---|---|---|
| Framework | Xamarin.Forms 5 (support ended May 1, 2024) | .NET MAUI on .NET 10 |
| Projects | shared project plus one project per platform | one app project plus a core library |
| Dependencies | `DependencyService` as a service locator | constructor injection |
| Platform services | one implementation per platform | one implementation on .NET MAUI APIs |
| Messaging | `MessagingCenter` with string keys | typed messages through `IMessenger` |
| Navigation | view models create pages and push them | Shell routes behind an `INavigator` interface |
| Custom control | one custom renderer per platform | one handler mapping |
| View models | reference Xamarin.Forms | reference no UI framework at all |
| JSON | Json.NET | System.Text.Json |

## Layout

```
scenarios/                         11 scenarios and the backend starter, shared by both test projects
before/ParcelTracker/              Xamarin.Forms shared project: pages, view models, API client
before/ParcelTracker.Android/      platform sources (entry point, renderer, services), see before/PLATFORM-PROJECTS.md
before/ParcelTracker.iOS/          platform sources
before/tests/                      the scenarios against the Xamarin.Forms app
after/src/ParcelTracker.Core/      view models, API client and service interfaces, plain .NET 10
after/src/ParcelTracker.App/       the .NET MAUI app: pages, Shell, handler mapping, platform services
after/tests/                       the scenarios against the core library
```

## Run it

Both test suites run on any OS with the .NET 10 SDK. No device, emulator or workload is needed.

```bash
dotnet test before/tests/ParcelTracker.ScenarioTests
dotnet test after/tests/ParcelTracker.ScenarioTests
```

Building the app itself needs the .NET MAUI workload:

```bash
dotnet workload install maui-android
dotnet build after/src/ParcelTracker.App -f net10.0-android
```

## What is verified, and what is not

| Check | Where | Result |
|---|---|---|
| 11 scenarios against the Xamarin.Forms pages and view models | CI, Linux | 11 / 11 |
| 11 scenarios against the .NET MAUI view models | CI, Linux | 11 / 11 |
| .NET MAUI app builds for Android | CI, Linux | builds, produces a signed APK |
| .NET MAUI app builds for Windows | CI, Windows | builds |
| .NET MAUI app builds for iOS | CI, macOS | IOS_RESULT |

Not verified:

- **The Xamarin platform projects.** The Xamarin toolchain is not available on current build agents. The Android and
  iOS sources in `before/` are reference code and were not compiled. The shared project, which holds all pages and
  view models, is compiled and tested.
- **The app on a device.** Nothing here was run on a phone or an emulator. The scenarios exercise view models, and for
  the Xamarin.Forms version also the real pages on a mocked platform layer. Layout, the borderless entry and the
  reading of settings stored by the old app are implemented as described below but were not observed on a device.

## How the same tests run against both versions

Both apps keep the same names for view models and their members: `ShipmentListViewModel.Items`, `LoadCommand`,
`ShipmentDetailViewModel.CanMarkDelivered`, and so on. [`scenarios/Scenarios.cs`](scenarios/Scenarios.cs) is one
source file that both test projects compile. Everything version-specific sits behind a class called `AppHarness`,
which each test project implements:

- **Before:** the harness creates the real `App`, with its real pages, on `Xamarin.Forms.Mocks`. It replaces the two
  per-platform services through `DependencyService` and runs everything on one dedicated thread that stands in for
  the UI thread.
- **After:** the harness builds the view models from a dependency injection container and supplies in-memory
  implementations of navigation, dialogs, connectivity and settings. No UI framework is loaded.

Neither harness fakes the API. Each test starts the shipment service from case 02 on a free port and the app talks to
it over HTTP, so the API clients of both versions are tested as well.

## What changed, file by file

| Xamarin.Forms | .NET MAUI |
|---|---|
| `App.xaml.cs`: `DependencyService.Register`, `MainPage = new NavigationPage(...)` | `MauiProgram.cs`: service registrations; `App.CreateWindow` returns the Shell |
| `[assembly: Dependency]` services in each platform project | `PlatformServices.cs`: one implementation each, on `Connectivity` and `Preferences` |
| `[assembly: ExportRenderer]` and a renderer class per platform | `EntryHandler.Mapper.AppendToMapping` with `#if ANDROID`, `#if IOS` |
| `MessagingCenter.Subscribe/Send` with the string `"ShipmentChanged"` | `IMessenger` with the record `ShipmentChangedMessage` |
| `Application.Current.MainPage.Navigation.PushAsync(new ShipmentDetailPage(id))` | `INavigator.ShowDetailAsync(id)`, implemented with `Shell.Current.GoToAsync` |
| `Application.Current.MainPage.DisplayAlert` | `IDialogs.AlertAsync` |
| `Device.BeginInvokeOnMainThread` around collection updates | removed: the code after `await` already runs on the UI thread |
| `Command` wrapping `async void` lambdas | generated `IAsyncRelayCommand` from `[RelayCommand]` |
| `Color.FromHex`, `StackLayout`, `FontSize="Large"` | `Color.FromArgb`, `VerticalStackLayout`, a numeric font size |
| XAML namespace `http://xamarin.com/schemas/2014/forms` | `http://schemas.microsoft.com/dotnet/2021/maui`, with compiled bindings (`x:DataType`) |

## What the scenarios found in the old app

- **The list loaded several times per appearance.** A trace of the Xamarin.Forms page showed two loads before the
  page had appeared and more when it did. Two bindings caused it: the status picker wrote its selection back into
  the view model, whose setter reloaded without checking whether the value had changed, and the pull-to-refresh
  control ran the load command again every time `IsBusy` became true. On a device this is redundant network traffic.
  In the first test run, without a UI thread, the overlapping loads duplicated the list items and then hung the run.
- **An alert shown from a view model never completed in the test.** `DisplayAlert` queues the alert until the page is
  attached to a platform renderer. The harness has to mark the page as platform-enabled.

Neither is a problem of the migration itself. Both are reasons why the old view models were hard to test, and both
are gone in the new version: the generated property setter ignores unchanged values, the refresh indicator has its
own property that is only ever reset, and dialogs are an interface.

## Two details that decide whether users notice the update

- **The application identifier is unchanged** (`com.example.parceltracker`). With a different identifier the stores
  treat the .NET MAUI build as a new app, and every user has to install it again.
- **Settings written by the old app are still read.** The Xamarin app stored the device identity and the offline
  cache in an Android `SharedPreferences` file named `parceltracker`. .NET MAUI `Preferences` uses its own file by
  default, so `PreferencesSettings` passes the old name explicitly. On iOS both versions use the standard user
  defaults. This is implemented but, as noted above, not verified on a device.

## One thing that went wrong on the new side

`CommunityToolkit.Mvvm` 8.4.0 generated nothing for partial properties under the .NET 10 SDK, and the build failed
with "partial property must have an implementation part". Version 8.4.2 works.

## Write-up

WRITEUP_LINKS

## Background

This case belongs to the guide
[Mobile App Modernization: Choosing a Path for Xamarin, Native and Cross-Platform Apps](https://legacymodernizationhub.com/guides/mobile-app-modernization-guide/).

Maintained by [Chudovo](https://chudovo.com/).
