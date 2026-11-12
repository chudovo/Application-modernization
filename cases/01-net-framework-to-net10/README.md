# Case 01: .NET Framework 4.8 Web API to .NET 10

A small order API, written the way many internal .NET Framework services were written, and the same API after
migration to .NET 10. Both versions must pass the same HTTP contract, so the migration is checked from the
client's point of view and not by reading the code.

| | Before | After |
|---|---|---|
| Runtime | .NET Framework 4.8 | .NET 10 |
| Web | ASP.NET Web API 2 on OWIN | ASP.NET Core controllers |
| Data access | Entity Framework 6, lazy loading | EF Core 10, explicit `Include` |
| JSON | Json.NET (Newtonsoft) | System.Text.Json |
| Configuration | `ConfigurationManager`, static `AppSettings` | `appsettings.json`, options |
| Dependencies | static service locator | built-in dependency injection |
| Time | static `Clock.UtcNow` delegate | injected `TimeProvider` |
| Request pipeline | `DelegatingHandler` | middleware |
| Calls | synchronous | `async` with cancellation |
| Tests | serial, LocalDB | parallel, in-memory SQLite |

## Layout

```
contract/cases.json   17 HTTP cases: request, expected status, expected body
before/               the legacy application and its contract tests
after/                the migrated application and its contract tests
```

## Run it

After (any OS with the .NET 10 SDK):

```bash
cd after
dotnet test
```

Before (Windows with SQL Server LocalDB; the .NET 10 SDK builds the .NET Framework 4.8 projects):

```bash
cd before
sqllocaldb start MSSQLLocalDB
dotnet test
```

Both run in CI on every push: the legacy application on `windows-latest`, the migrated one on `ubuntu-latest`.

## The contract comes first

Before any code moved, the behaviour of the legacy API was written down as data in
[`contract/cases.json`](contract/cases.json): status codes, exact property names, error bodies, the `Location`
header. Property names are compared case-sensitively, because `Id` and `id` are different contracts for a client
that deserializes into a typed model.

The legacy test suite runs this file against Web API 2. The migrated test suite runs the same file against
ASP.NET Core. Nothing in the file knows which framework answers.

## What the contract caught

The first port of the controllers compiled and looked right. Against the contract it passed 0 of 17 cases.

| Step | Passing | What was wrong |
|---|---|---|
| Straight port, framework defaults | 0 / 17 | every response body used `camelCase` property names |
| Keep property names as declared | 14 / 17 | three differences left, below |
| Compatibility settings | 17 / 17 | |

The three differences after fixing the naming:

1. **404 with a body.** `NotFound()` in a controller marked `[ApiController]` returns a ProblemDetails JSON body.
   The legacy API returned 404 with an empty body.
2. **Invalid JSON never reached the action.** `[ApiController]` answers a malformed body itself, with a
   ProblemDetails body listing validation errors. The legacy API ran the action and returned
   `{ "Message": "..." }`.
3. **The API key error was camel-cased.** Responses written from middleware use a different set of JSON options
   than MVC, so the naming fix for controllers did not apply to it.

All three are fixed in [`after/src/OrderDesk.Api/Program.cs`](after/src/OrderDesk.Api/Program.cs), each with a
comment that says which legacy behaviour it preserves.

## What did not break, although it often does

- **Numbers sent as strings.** Old clients send `"Quantity": "3"`. Json.NET accepts that. System.Text.Json
  rejects it by default, but ASP.NET Core configures it with web defaults, which accept it. The contract has a
  case for it, and it passed without changes.
- **Case-insensitive request property names.** Same reason: web defaults.
- **Money.** Totals are rounded in the domain code with an explicit `MidpointRounding.AwayFromZero`, so they do
  not depend on the JSON library or the database provider.
- **UTC timestamps.** Both databases return `DateTime` with `Kind=Unspecified`. The DTO mapping sets the kind
  explicitly in both versions, otherwise the `Z` suffix disappears from the JSON.

## Changes that the contract cannot see

These do not change any response, which is the point: they were made while the contract stayed green.

- `ConfigurationManager.AppSettings` became a bound options class that is passed into `PricingService`.
- The static `ServiceLocator` and the parameterless controller constructor are gone. Dependencies are
  constructor parameters.
- `Clock.UtcNow`, a static delegate that tests had to overwrite, became an injected `TimeProvider`.
- The EF6 context was created inside every service method and relied on lazy loading. A helper called `Touch`
  forced navigation properties to load before the context was disposed. EF Core has one context per request and
  loads related data with `Include`.
- EF6 maps `decimal` to `decimal(18,2)` by default. EF Core has no default, so the precision the existing
  schema uses is stated in `OnModelCreating`.
- The legacy tests cannot run in parallel, because the application state is static. The migrated tests run in
  parallel, each with its own in-memory database.

## What this example leaves out

- Hosting on IIS with `System.Web`, `Global.asax` and `HttpContext.Current`. The legacy application here is
  hosted on OWIN, which keeps the example small and lets the contract tests host it in memory.
- Authentication beyond an API key header.
- Running the migrated application against the database that EF6 created. The schema is intended to be
  compatible, but that is not tested here.
- WCF, Web Forms and Windows services. Each needs a different approach and gets its own case.

## Write-up

A step-by-step description of this case, with the reasoning behind each decision:

- English: [.NET Framework 4.8 Web API to .NET 10: A Worked Migration](https://legacymodernizationhub.com/cases/net-framework-web-api-to-net-10/)
- Deutsch: [Von .NET Framework 4.8 zu .NET 10: Migration einer Web-API am Beispiel](https://legacy-modernisierung.de/fallbeispiele/net-framework-web-api-zu-net-10/)

## Background

This case belongs to the guide
[Application Modernization Strategy: Choosing the Path for One Application](https://legacymodernizationhub.com/guides/application-modernization-strategy-guide/).
The guide explains when a move like this one, a rehost onto a new runtime with targeted refactoring, is the
right path and when it is not.

Maintained by [Chudovo](https://chudovo.com/legacy-migration/legacy-net-migration/).
