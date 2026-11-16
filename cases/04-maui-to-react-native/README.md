# Case 04: .NET MAUI to React Native

The Parcel Tracker app from [case 03](../03-xamarin-forms-to-maui/), written with .NET MAUI, and the same app rewritten
in React Native with TypeScript. The app is a client of the shipment service from [case 02](../02-wcf-to-web-api/).

This is a rewrite, not an upgrade: none of the C# is reused. What carries over is the behaviour. The 11 screen-level
scenarios of the .NET MAUI app are ported one for one and run against the rendered React Native screens and the real
service over HTTP.

| | Before | After |
|---|---|---|
| Framework | .NET MAUI on .NET 10 | React Native 0.88 with Expo SDK 58 |
| Language | C# and XAML | TypeScript (strict) |
| Screen logic | view models with commands and bindings | function components with hooks |
| Dependencies | constructor injection | one `Services` object in React context |
| Navigation | Shell routes behind `INavigator` | React Navigation native stack |
| Messaging between screens | typed messages through `IMessenger` | a small typed event |
| Settings on the device | `Preferences` | AsyncStorage, filled once from the old store |
| Tests | xUnit against view models | Jest against rendered screens |

## Layout

```
before/                          pointer to the .NET MAUI app in cases/03-xamarin-forms-to-maui/after
after/src/screens/               the three screens
after/src/services/              API client, services context, event, device implementations, settings migration
after/src/numbers.ts             reading numbers the way the user's device writes them
after/modules/legacy-settings/   native module (Kotlin, Swift) that reads what the .NET MAUI app stored
after/__tests__/                 11 screen scenarios, 4 migration tests
after/test-support/              starts the real service, renders the app against it
tools/TestBackend/               hands out a fresh copy of the case 02 service per test
```

## Run it

The tests need Node 22 and the .NET 10 SDK (it runs the service). No phone, emulator or Android SDK is needed.

```bash
cd after
npm ci
npm test
npm run typecheck
```

Building the app for Android needs JDK 17 and the Android SDK (for iOS: a Mac with Xcode, `npx expo prebuild --platform ios`):

```bash
cd after
npx expo prebuild --platform android
cd android && ./gradlew assembleDebug
```

## What is verified, and what is not

| Check | Where | Result |
|---|---|---|
| 11 scenarios against the React Native screens and the real service | CI, Linux | 11 / 11 |
| 4 tests of the settings migration | CI, Linux | 4 / 4 |
| TypeScript type check, strict mode | CI, Linux | passes |
| App builds for Android, native settings module included | CI, Linux | builds a debug APK, module found in it |
| App builds for the iOS simulator | CI, macOS | builds, unsigned, module compiled |

Not verified:

- **The app on a device.** The scenarios render the real screens and navigation in Jest, not on a phone. Layout,
  gestures and the native reading of the old preferences file were not observed on a device. Before a release, do the
  manual check once: install the .NET MAUI build, create data, install the React Native build over it.
- **Shared tests.** In case 03 one source file is compiled against both versions. Across two languages that is not
  possible: the eleven tests were rewritten by hand with the same names, data and expected texts
  ([C#](../03-xamarin-forms-to-maui/scenarios/Scenarios.cs), [TypeScript](after/__tests__/scenarios.test.tsx)).
- **Performance, app size and start-up time** were not compared.

## How the scenarios run

In the .NET MAUI version the scenarios call view model commands. React Native has no view models, so the ported
scenarios render `AppRoot` (navigation and all three screens) with `@testing-library/react-native` and work through
what is on screen:

```ts
await fireEvent.press(screen.getByTestId('shipment-SHP-000002'));
await screen.findByText('Jean Martin');

await press('Mark delivered');

await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('Delivered'));
expect(screen.getByTestId('delivered')).toHaveTextContent('Delivered 2026-02-10 12:00 UTC');
```

The API is not faked. `test-support/globalSetup.js` builds and starts `tools/TestBackend`, a control server that
creates a fresh instance of the case 02 service (with a fixed clock) for every test. Only four device-level services
are replaced: connectivity, key-value storage, dialogs and the device locale. See
[`test-support/harness.tsx`](after/test-support/harness.tsx).

## Where a direct translation is wrong

Take either of these two pieces out of the finished app and scenarios fail (three of eleven for both together).

**The decimal comma.** The form reads a weight. In C# it was `decimal.TryParse(text, culture)`. In JavaScript
`Number("2,5")` is `NaN` on every device, so [`numbers.ts`](after/src/numbers.ts) takes the decimal separator from the
device locale. Caught by *a weight typed with a decimal comma is understood on a German device*.

**The list behind the detail screen.** A native stack keeps the list mounted while the detail screen is open, so it
does not reload on the way back. The list subscribes to [`shipmentChanged`](after/src/services/events.ts), the
counterpart of the typed message in the .NET MAUI app. Caught by *marking as delivered updates the detail and the list
behind it* and *saving creates the shipment under the device identity and opens its detail*.

## What the old app left on the device

The application identifier (`com.example.parceltracker`) is unchanged, so the stores deliver the new app as an update.
The update must not lose the device identity and the offline cache:

- The .NET MAUI app stored both in a SharedPreferences file named `parceltracker` on Android and in the standard user
  defaults on iOS. JavaScript storage libraries cannot see either. The local Expo module
  [`modules/legacy-settings`](after/modules/legacy-settings) reads them: about 20 lines of Kotlin and 10 of Swift.
- The old cache was written with System.Text.Json defaults: PascalCase names plus two computed properties.
  [`legacySettings.ts`](after/src/services/legacySettings.ts) converts it to the camelCase shape the API returns and
  runs once, on the first start after the update.

The TypeScript half is covered by four tests that use a cache string in the format the old app wrote. The native half
is compiled into the Android and iOS builds in CI and was not run on a phone.

## Notes from the test setup

- The Jest environment of Expo replaces the global `fetch`, so a test that should reach a real service gets no network.
  `createShipmentApi(baseUrl, fetchImpl)` takes the function as a parameter; tests pass one built on `node:http`
  ([`test-support/http.ts`](after/test-support/http.ts)).
- In `@testing-library/react-native` 14, `fireEvent` calls are asynchronous. A press that is not awaited after a text
  change sees the old state, which first looked like a bug in the form.
- The hosted build agents had the .NET 11 SDK preinstalled, and it failed to build the test backend (a static web
  assets error). [`global.json`](global.json) pins the .NET 10 SDK for this case.
