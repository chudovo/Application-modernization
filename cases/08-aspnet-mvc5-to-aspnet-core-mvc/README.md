# Case 08: ASP.NET MVC 5 to ASP.NET Core MVC

A server-rendered web application for seminar registrations, written with ASP.NET MVC 5 on .NET Framework 4.8, and the
same application on ASP.NET Core MVC with .NET 10. It is a German application: prices, dates and the numbers users
type follow German conventions.

One contract of 21 checks is written against HTTP and HTML only. It runs against the old application under IIS
Express on Windows and against the new one under Kestrel on Linux.

| | Before | After |
|---|---|---|
| Framework | ASP.NET MVC 5.3, .NET Framework 4.8 | ASP.NET Core MVC, .NET 10 |
| Host | IIS / IIS Express, Windows | Kestrel, any OS |
| Configuration | `Web.config`, `Global.asax` | `Program.cs` |
| Culture | `<globalization culture="de-DE" />` | request localization middleware, fixed to de-DE |
| Views | HTML helpers (`Html.TextBoxFor`, `Html.BeginForm`) | tag helpers (`asp-for`, `asp-action`) |
| Part of the layout | child action (`Html.Action`) | view component |
| Sign-in | forms authentication | cookie authentication |
| JSON | `Json(...)` with JavaScriptSerializer | System.Text.Json, configured to the old shape |

## Layout

```
contract/                   21 checks and a minimal browser (cookies, forms, hidden fields), shared by both versions
before/src/Seminars.Web/    the ASP.NET MVC 5 application
after/src/Seminars.Web/     the ASP.NET Core MVC application
```

## Run it

The new application and the contract need only the .NET 10 SDK:

```bash
dotnet run --project after/src/Seminars.Web --urls http://localhost:5080
```

```bash
dotnet test contract
```

The old application needs Windows with IIS Express. Build it with `dotnet build before/src/Seminars.Web`, then:

```bash
"C:\Program Files\IIS Express\iisexpress.exe" /path:<full path to before\src\Seminars.Web> /port:5080 /clr:v4.0
```

The contract talks to `http://localhost:5080` unless `SEMINARS_URL` says otherwise.

## What is verified, and what is not

| Check | Where | Result |
|---|---|---|
| Contract against ASP.NET MVC 5 | CI, Windows, IIS Express | @@BEFORE@@ |
| Contract against ASP.NET Core MVC | CI, Linux, Kestrel | @@AFTER@@ |

Not verified:

- **The bytes.** The contract compares what a browser shows and what a form posts, not the raw HTML. The raw output
  differs in places that do not matter to a browser: MVC 5 writes `ü` as `&#252;`, ASP.NET Core as `&#xFC;`; MVC 5
  writes `ü` in JSON as it is, ASP.NET Core as `\u00FC`. A client that reads the HTML or JSON as plain text instead
  of parsing it would notice.
- **Signed-in users across the switch.** The authentication cookie has another name and format. Everyone is signed
  out once when the new version goes live. The same is true for anti-forgery tokens in forms that are open in a
  browser at that moment.
- **JavaScript.** The pages carry the attributes for client-side validation and the contract checks them, but no
  script runs in the tests.
- **What this small application does not use:** bundling, areas, session state, output caching, custom HTTP modules,
  Windows authentication. Each has its own migration path.

## What a straightforward port got wrong

The first version of the new application was a direct translation: same controllers, same views with tag helpers,
default settings. Against the contract, **11 of 21 checks failed**. The causes were three.

**1. The culture was gone.** The old `Web.config` has one line, `<globalization culture="de-DE" uiCulture="de-DE" />`.
ASP.NET Core has no such setting, and a Linux container runs with the invariant culture.

| | Old application | Straightforward port |
|---|---|---|
| Price on the home page | `1.249,50 €` | `¤1,249.50` |
| User types a budget of `750,5` | stored as 750.50 | stored as **7505.00**, no error |
| User types `1500,00` | stored as 1500.00 | refused as out of range |

The middle row is the dangerous one: a valid German amount, accepted, and wrong by a factor of ten.
[`Program.cs`](after/src/Seminars.Web/Program.cs) adds request localization fixed to de-DE, with the providers that
look at the browser's language removed, because the old application ignored the browser too.

**2. The JSON changed shape.** `Json(...)` in MVC 5 writes property names as declared and dates as
`/Date(1770109200000)/`. ASP.NET Core writes camelCase names and ISO dates. A partner site parses the old shape, so
the new application is configured to produce it.

**3. Decimals are read more generously.** With the culture fixed, **2 of 21 checks still failed**. MVC 5 accepts a
decimal separator and nothing else. ASP.NET Core also accepts thousands separators. On a German system the point is
the thousands separator:

| User types | Old application | ASP.NET Core with de-DE |
|---|---|---|
| `750.50` | validation message | stored as **75050.00**, no error |
| `1.500,00` | validation message | stored as 1500.00 |

Accepting `1.500,00` is arguably better. Turning `750.50` into seventy-five thousand is not, and the two come
together. [`LegacyDecimalModelBinder`](after/src/Seminars.Web/Infrastructure/LegacyDecimalModelBinder.cs) reads
decimals the way the old application did. Whether to relax that is a product decision to take on purpose, later.

## What translated without surprises

These were ported mechanically and passed the contract on the first run:

- routing, including addresses in another letter case and with a trailing slash
- model validation with data annotations, the messages and the `data-val-*` attributes for client-side validation
- HTML encoding of data that contains markup
- the redirect to the login page with `ReturnUrl`, and the refusal to redirect to another site after sign-in
- `TempData` surviving exactly one redirect
- anti-forgery protection (the old application answers a missing token with 500, the new one with 400; the contract
  only requires a refusal)
- the child action in the layout, as a view component

## A note on the old application's project file

`before/` uses an SDK-style project file targeting `net48`, so it builds with `dotnet build` on any OS. It still only
runs under IIS or IIS Express. One thing went wrong on the first run under IIS Express: the Razor views are UTF-8
without a byte order mark, and ASP.NET read them in the system code page (`KursÃ¼bersicht`).
`fileEncoding="utf-8"` in `Web.config` fixes that. Visual Studio hides this by saving views with a mark.
