# Application modernization cases

Small, runnable examples of modernizing legacy applications. Each case contains the application before the
change, the application after it, and tests that both versions must pass.

The cases accompany the guides on [legacymodernizationhub.com](https://legacymodernizationhub.com/guides/)
and are maintained by [Chudovo](https://chudovo.com/).

| # | Case | From | To | Write-up |
|---|---|---|---|---|
| 01 | [.NET Framework Web API to .NET 10](cases/01-net-framework-to-net10/) | .NET Framework 4.8, Web API 2, EF6 | .NET 10, ASP.NET Core, EF Core | [EN](https://legacymodernizationhub.com/cases/net-framework-web-api-to-net-10/), [DE](https://legacy-modernisierung.de/fallbeispiele/net-framework-web-api-zu-net-10/) |
| 02 | [WCF service to ASP.NET Core Web API](cases/02-wcf-to-web-api/) | .NET Framework 4.8, WCF, SOAP | .NET 10, REST API, SOAP kept through CoreWCF | [EN](https://legacymodernizationhub.com/cases/wcf-to-aspnet-core-web-api/), [DE](https://legacy-modernisierung.de/fallbeispiele/wcf-zu-aspnet-core-web-api/) |
| 03 | [Xamarin.Forms to .NET MAUI](cases/03-xamarin-forms-to-maui/) | Xamarin.Forms 5 | .NET MAUI on .NET 10 | [EN](https://legacymodernizationhub.com/cases/xamarin-forms-to-net-maui/), [DE](https://legacy-modernisierung.de/fallbeispiele/xamarin-forms-zu-net-maui/) |
| 04 | [.NET MAUI to React Native](cases/04-maui-to-react-native/) | .NET MAUI on .NET 10 | React Native 0.88, Expo SDK 58 | [EN](https://legacymodernizationhub.com/cases/net-maui-to-react-native/), [DE](https://legacy-modernisierung.de/fallbeispiele/net-maui-zu-react-native/) |
| 05 | [Entity Framework 6 to EF Core](cases/05-ef6-to-ef-core/) | Entity Framework 6.5, SQL Server | EF Core 10, same database | [EN](https://legacymodernizationhub.com/cases/entity-framework-6-to-ef-core/), [DE](https://legacy-modernisierung.de/fallbeispiele/entity-framework-6-zu-ef-core/) |
| 06 | [Strangler Fig: one service out of a monolith](cases/06-strangler-fig/) | ASP.NET Core monolith, one database | gateway (YARP), monolith, catalog service | [EN](https://legacymodernizationhub.com/cases/strangler-fig-pattern-example/), [DE](https://legacy-modernisierung.de/fallbeispiele/strangler-fig-muster-beispiel/) |
| 07 | [Zero-downtime schema change (expand and contract)](cases/07-zero-downtime-schema-change/) | PostgreSQL, money as floating point | integer cents, changed under load | [EN](https://legacymodernizationhub.com/cases/zero-downtime-schema-change-expand-contract/), [DE](https://legacy-modernisierung.de/fallbeispiele/schemaaenderung-ohne-downtime-expand-contract/) |
| 08 | [ASP.NET MVC 5 to ASP.NET Core MVC](cases/08-aspnet-mvc5-to-aspnet-core-mvc/) | ASP.NET MVC 5.3, .NET Framework 4.8, IIS | ASP.NET Core MVC, .NET 10, Kestrel | [EN](https://legacymodernizationhub.com/cases/aspnet-mvc-5-to-aspnet-core-mvc/), [DE](https://legacy-modernisierung.de/fallbeispiele/aspnet-mvc-5-zu-aspnet-core-mvc/) |
| 09 | [Containerizing a Windows-hosted service](cases/09-containerize-a-windows-hosted-service/) | a service on one Windows server | a Linux container: non-root, read-only, replaceable | [EN](https://legacymodernizationhub.com/cases/containerizing-a-legacy-net-service/), [DE](https://legacy-modernisierung.de/fallbeispiele/legacy-net-dienst-containerisieren/) |
| 10 | [Transactional outbox](cases/10-transactional-outbox/) | save to the database, then publish to the broker | outbox table, relay, idempotent consumer (PostgreSQL, RabbitMQ) | [EN](https://legacymodernizationhub.com/cases/transactional-outbox-pattern-example/), [DE](https://legacy-modernisierung.de/fallbeispiele/transactional-outbox-muster-beispiel/) |

## License

MIT. See [LICENSE](LICENSE).
