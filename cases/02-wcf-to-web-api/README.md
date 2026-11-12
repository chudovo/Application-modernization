# Case 02: WCF service to ASP.NET Core Web API

A shipment service written as a WCF service on .NET Framework 4.8, and the same service on .NET 10 with a REST API.
The SOAP contract stays available on .NET 10 through CoreWCF, so existing clients keep working while they are moved
to REST one by one.

One file of 16 scenarios is run three times: against the legacy WCF service, against the new SOAP endpoint and
against the new REST API.

| | Before | After |
|---|---|---|
| Runtime | .NET Framework 4.8 | .NET 10 |
| Protocol | SOAP over HTTP, `basicHttpBinding` | REST with JSON, plus the same SOAP contract through CoreWCF |
| Where the rules live | inside the WCF service class | a core library that knows nothing about SOAP, HTTP or JSON |
| Errors | typed SOAP faults | domain exceptions, mapped to faults for SOAP and to ProblemDetails for REST |
| Caller identity | read from the ambient `OperationContext` | an explicit parameter |
| State | static class behind a lock | an injected singleton |
| Time | static delegate | injected `TimeProvider` |
| Configuration | `<system.serviceModel>` in App.config | code in `ShippingApp.Build` |

## Layout

```
contract/scenarios.json   16 scenarios: operation, arguments, expected result or error
before/                   the WCF service, its host and its scenario tests
after/                    the core library, the API with REST and SOAP endpoints, and its scenario tests
```

## Run it

After (any OS with the .NET 10 SDK):

```bash
cd after
dotnet test
```

Before (Windows; the .NET 10 SDK builds the .NET Framework 4.8 projects):

```bash
cd before
dotnet test
```

The legacy tests open a real `ServiceHost` on a free local port, which needs permission to listen on HTTP
(an elevated prompt or a URL reservation). Both versions run in CI on every push.

## The scenarios are protocol-neutral

SOAP and REST cannot share an HTTP-level contract, so the scenarios describe operations instead:
"create a shipment with these fields", "expect this result" or "expect a not-found error". Each test suite has a
small client that performs the operation over its protocol and turns the answer into the same neutral shape.

| Run | Client | Result |
|---|---|---|
| Legacy WCF service on .NET Framework 4.8 | `ChannelFactory<IShipmentService>` | 16 / 16 |
| New SOAP endpoint on .NET 10 (CoreWCF) | `ChannelFactory<IShipmentService>` from `System.ServiceModel.Http` | 16 / 16 |
| New REST API on .NET 10 | `HttpClient` | 16 / 16, plus one REST-only test |

The second row is the one that matters for a migration plan: a client built for the old service calls the new one
without being recompiled against a new contract.

## How the operations map to REST

| WCF operation | REST |
|---|---|
| `GetShipment(trackingNumber)` | `GET /api/shipments/{trackingNumber}` |
| `FindShipments(query)` | `GET /api/shipments?status=&countryCode=` |
| `CreateShipment(request)` | `POST /api/shipments`, answers 201 with a `Location` header |
| `UpdateStatus(trackingNumber, newStatus)` | `PUT /api/shipments/{trackingNumber}/status` |
| `QuoteRate(weightKg, countryCode, service)` | `POST /api/rates/quote` |

| SOAP fault | REST |
|---|---|
| `FaultException<NotFoundFault>` | 404, `code: shipment_not_found` |
| `FaultException<ValidationFault>` | 400, `code: validation_failed`, `errors: [...]` |
| `FaultException<InvalidTransitionFault>` | 409, `code: invalid_transition`, `from`, `to` |

`QuoteRate` is a calculation with no resource behind it. It stays an RPC-style `POST` instead of being forced into
a resource shape.

## The order of work

1. **Write the scenarios against the legacy service.** They passed 16 of 16 on .NET Framework before anything moved.
2. **Pull the rules out of the service class.** `ShipmentService` contained validation, pricing, the status rules,
   fault mapping and the header lookup. In `after/` that is `Shipping.Core`, where errors are plain exceptions and the
   caller is a parameter.
3. **Host the unchanged contract on .NET 10.** `after/src/Shipping.Contracts.Soap` compiles the same
   `IShipmentService.cs` file that the legacy service uses. `SoapShipmentService` is an adapter: map the request, call
   the core, map the result or the fault.
4. **Add the REST API on the same core.** Both endpoints live in one application and share one store.
5. **Move clients, then remove the SOAP endpoint.** Not part of this example, because it depends on who the clients are.

## What went wrong on the way

- **CoreWCF pulled ASP.NET Core 2.x packages into the test project.** In a web project the shared framework replaces
  them. The test project is a plain class library, so NuGet restored the old packages, and the build failed on a known
  vulnerability in one of them. A `FrameworkReference` to `Microsoft.AspNetCore.App` in the test project fixes it.
- **A request body that cannot be bound got a bare 400.** Sending `{"status": "Lost"}` to the REST API returned
  status 400 with an empty body, while every other error returned a ProblemDetails document. With
  `RouteHandlerOptions.ThrowOnBadRequest` the failure goes through the same exception handler as the domain errors.
- **The legacy test suite deadlocked.** A `ServiceHost` opened on a thread that has a `SynchronizationContext`
  dispatches every call back to that context. The test runner has one, and the test thread was blocked in the client
  call. Opening the host on a thread-pool thread fixed it. The same behaviour bites WCF services hosted inside
  desktop applications.

What worked on the first run, although it often does not: the contract source file compiled for .NET 10 without
changes, typed faults reached the WCF client as the same `FaultException<T>` types, the optional `DeliveredUtc`
element was omitted and read back as `null`, and the HTTP header with the caller identity was available through
CoreWCF's `OperationContext`.

## What this example leaves out

- WS-Security, message-level encryption and transport security.
- `netTcpBinding`, duplex contracts, sessions and transactions. CoreWCF covers some of these, not all.
- WSDL publishing for the new SOAP endpoint.
- Generated client proxies (`svcutil`, service references). The tests use `ChannelFactory` with the shared contract.
- A database. The store is in memory in both versions, to keep the example about the service layer.

## Write-up

- English: [WCF Service to ASP.NET Core Web API: A Worked Migration](https://legacymodernizationhub.com/cases/wcf-to-aspnet-core-web-api/)
- Deutsch: [Von WCF zur ASP.NET-Core-Web-API: Migration am Beispiel](https://legacy-modernisierung.de/fallbeispiele/wcf-zu-aspnet-core-web-api/)

## Background

This case belongs to the guide
[Application Modernization Strategy: Choosing the Path for One Application](https://legacymodernizationhub.com/guides/application-modernization-strategy-guide/).

Maintained by [Chudovo](https://chudovo.com/legacy-migration/legacy-net-migration/).
