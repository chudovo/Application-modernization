# Case 06: Strangler Fig, one slice out of a monolith

A small shop as one application with one database, and the same shop after its catalog has been moved into a service
of its own behind a gateway. Clients keep one address and notice nothing.

The contract is 19 checks written against HTTP only. It runs three times: against the monolith, through the gateway
with everything still in the monolith, and through the gateway with the catalog served by the new service.

| | Before | After |
|---|---|---|
| Clients talk to | the monolith | a gateway (YARP) |
| `/api/products` | monolith, table `Products` | catalog service with its own database |
| `/api/orders`, `/api/stock` | monolith | monolith, unchanged address |
| Orders need product data | read the `Products` table directly | ask `ICatalog`, which calls the catalog service |
| Switching the catalog over | - | one routing change, no restart |

## Layout

```
scenarios/                    the contract and test helpers, shared by both test projects
before/src/Shop.Monolith/     catalog, stock and orders in one ASP.NET Core application
before/tests/                 the contract against the monolith
after/src/Shop.Gateway/       the facade: YARP with a routing table that can be changed at run time
after/src/Shop.Monolith/      the monolith with a seam (ICatalog) and forwarded headers
after/src/Shop.Catalog/       the new service, including the one-off copy of the catalog data
after/tests/                  the contract through the gateway in both routing states, plus five extraction tests
```

## Run it

Only the .NET 10 SDK is needed. The tests start every piece on a free local port.

```bash
dotnet test before/tests/Shop.Contract
dotnet test after/tests/Shop.Contract
```

## What is verified, and what is not

| Check | Where | Result |
|---|---|---|
| Contract against the monolith | CI, Linux | 19 / 19 |
| Contract through the gateway, everything routed to the monolith | CI, Linux | 19 / 19 |
| Contract through the gateway, catalog served by the new service | CI, Linux | 19 / 19 |
| Extraction tests: routing, data copy, catalog outage, routing back and forward | CI, Linux | 5 / 5 |

Not verified:

- **Load and latency.** Every catalog lookup made while placing an order is now a network call. Nothing here measures
  what that costs, and there is no cache, retry or circuit breaker.
- **Writes during the cutover.** The data copy can be repeated, but a product changed in the monolith between the last
  copy and the routing switch is lost. A real cutover needs a short write freeze or change capture.
- **Deployment.** The pieces run in one test process on local ports. No containers, no service discovery, no TLS
  between the services, and the in-memory SQLite databases stand in for real ones.
- **The other slices.** Stock and orders share a transaction and stay together in the monolith. Splitting them is a
  different and harder problem.

## The order of work

1. **Write the contract against the monolith.** [`scenarios/ContractScenarios.cs`](scenarios/ContractScenarios.cs)
   compares status codes, headers and bodies as text. It is what "clients notice nothing" means.
2. **Put the gateway in front, routing everything to the monolith.** No behaviour is supposed to change.
   The contract says whether that is true.
3. **Cut the seam inside the monolith.** The order code read the `Products` table directly. It now asks an interface,
   [`ICatalog`](after/src/Shop.Monolith/Catalog.cs), still answered from the local table.
4. **Build the new service** and run the contract against it through the gateway.
5. **Copy the data and switch**: the catalog paths in the gateway, and `Catalog:Mode` in the monolith.

## What the contract caught

Both numbers come from running the contract against the first working version of each step.

**Step 2, the gateway alone: 2 of 19 checks failed.** The monolith builds the `Location` header of a created resource
from the address it was called at. Behind the gateway that is its internal address:

```
Expected: http://127.0.0.1:39703/api/products/RUG-11     (the gateway, which the client talked to)
Actual:   http://127.0.0.1:36103/api/products/RUG-11     (the monolith)
```

YARP sends `X-Forwarded-Host` and `X-Forwarded-Proto` by default, but the application behind it has to be told to
use them (`UseForwardedHeaders`, in [`Program.cs`](after/src/Shop.Monolith/Program.cs)) and which proxy to believe.

**Step 4, the new service written the usual way: 10 of 19 checks failed.** Nothing was wrong with the service on
its own terms. It simply did things the way a new ASP.NET Core minimal API does them:

| Contract | First version of the new service |
|---|---|
| Prices with two decimals: `89.50` | `89.5` |
| Errors as `{"error":"..."}` for 400, 401, 404, 409 | empty bodies, or a validation problem document |
| `Location` as an absolute address | `/api/products/RUG-11` |
| Search ignores case | `Contains` is case-sensitive on this database, `LIKE` is not |

Each is one line to fix once it is known, see the comments marked `Contract:` in
[`Shop.Catalog/Program.cs`](after/src/Shop.Catalog/Program.cs). Without the contract they would have been found by
clients.

## What extraction adds that the monolith never had

Covered by [`ExtractionTests.cs`](after/tests/Shop.Contract/ExtractionTests.cs):

- **A dependency that can be down.** With the catalog service stopped, orders can still be read and stock queried.
  Placing an order returns 503 with a message and changes no stock. The catalog paths return 502 from the gateway.
- **A copy of the data.** [`Backfill`](after/src/Shop.Catalog/Program.cs) reads every product through the monolith's
  own API and can be run again; the second run picks up changes.
- **Routing back is immediate, data is not.** Switching the catalog paths back to the monolith takes effect on the
  next request. But a product created after the cutover exists only in the new service, and the monolith's table
  still has the old prices. The test asserts exactly that. A rollback plan needs the data to flow back, or the switch
  to be made at a moment when nothing has been written yet.
- **Two switches.** Routing in the gateway decides who answers clients. `Catalog:Mode` in the monolith decides whom
  the order code asks. After routing back alone they disagree: clients see the monolith's catalog while orders are
  still priced by the service. Change both, in a defined order.

## Why the catalog went first

It is read far more than written, it has one inbound dependency (orders need name, price and whether the product is
active), and it shares no transaction with anything. Stock looks similar but is changed inside the order transaction,
so it stays where the orders are.
