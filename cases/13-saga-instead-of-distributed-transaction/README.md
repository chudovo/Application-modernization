# Case 13: A saga where a database transaction used to be

Placing an order takes goods out of stock, takes money, and confirms the order: all three or none. In a monolith
that is one database transaction. After the monolith is split into an order, an inventory and a payment service, each
with its own database, there is no transaction that covers all three.

Three versions of the same process, and one set of books to check them: the monolith, the first version after the
split, and a saga.

| | Monolith | First attempt | Saga |
|---|---|---|---|
| Databases | one | three | three |
| All or nothing by | a transaction | a `catch` block that undoes by hand | stored steps with compensations |
| Process dies in the middle | rolled back | left half done, forever | continued by the next process |
| A service answers too late | cannot happen | retried: charged twice | retried: steps can be repeated |
| Undoing fails | cannot happen | given up | repeated until it succeeds |
| What the caller is told | confirmed or rejected | confirmed, rejected, or an exception | confirmed, rejected, or pending |

## Layout

```
database/monolith.sql                  one schema: stock, orders, payments
database/services.sql                  three schemas, one per service
database/saga.sql                      what the saga adds: a unique index on payments, the table of saga positions
src/Shop/Before/Monolith.cs            before
src/Shop/Attempt/NaiveDistributed.cs   the first version after the split
src/Shop/After/Services.cs             inventory and payment services whose operations can be repeated
src/Shop/After/Saga.cs                 the saga
tests/Shop.Tests/                      5 scenarios per design, failure tests, 300 orders under mixed failures
```

## Run it

You need the .NET 10 SDK and a PostgreSQL server. With Docker:

```bash
docker run -d -e POSTGRES_PASSWORD=local_dev_only -p 54320:5432 postgres:17
```

```bash
dotnet test tests/Shop.Tests
```

Another server: set `SHOP_PG`. The tests create and drop their own databases.

## What is verified, and what is not

| Check | Where | Result |
|---|---|---|
| 5 scenarios without failures, for each of the three designs | CI, PostgreSQL 17 | @@SCEN@@ |
| 4 tests that describe what the first attempt does when something fails | CI | @@ATT@@ |
| 6 tests of what the saga does when something fails | CI | @@SAGA@@ |
| 300 orders under mixed failures, all three designs | CI | see below |

Not verified:

- **Real services and a real network.** The three services are classes in one process, each with its own database
  schema and its own connection pool. A call between them is a method call with failures injected at named points:
  not reached, answered too late, caller died. No HTTP, no message broker, no real timeouts.
- **Real crashes.** A crash is an exception after which none of the following code runs. No process is killed.
- **Isolation.** A saga has none: between its steps other requests see goods reserved for an order that may still be
  rejected. The scenarios show that the final state is right, not that every intermediate state is harmless.
- **Compensations that cannot be done.** A refund is always possible here. An e-mail that was sent, or goods that
  left the warehouse, cannot be taken back, and the order of steps has to be chosen with that in mind.

## The first attempt is correct until a machine fails

[`NaiveDistributed.cs`](src/Shop/Attempt/NaiveDistributed.cs) is the body of the old transaction with the statements
replaced by calls to the two new services, and a `catch` block that undoes what was done:

```csharp
try
{
    foreach (var line in lines) await inventory.ReserveAsync(orderId, line);
    await payments.CaptureAsync(orderId, card, total);        // with one retry if the service does not answer
    await orders.ExecuteAsync("UPDATE orders.orders SET status = 'confirmed' WHERE id = $1", orderId);
}
catch (Exception e) when (e is BusinessRuleException or ServiceUnavailableException)
{
    await inventory.ReleaseAsync(orderId);                     // undo by hand what a rollback used to undo
    await orders.ExecuteAsync("UPDATE orders.orders SET status = 'rejected', reason = $2 WHERE id = $1", orderId, e.Message);
}
```

It passes all five scenarios: a confirmed order, a declined card, a line out of stock, five orders for the last lamp,
a repeated request. Reviews pass it, and so does a demo. Four tests show what it does otherwise:

- **The process dies after reserving.** The goods are gone, the order stays pending, and nothing knows that there is
  something to finish.
- **The process dies after charging.** The customer has paid for an order that is not confirmed.
- **The payment service answers too late.** The capture happened; the caller is told it failed and tries again.
  The order is confirmed, everything looks fine, and the customer is charged twice.
- **Undoing fails too.** The card is declined, the release of the goods does not get through, the exception leaves
  the method, and the goods of a rejected order stay reserved.

## The saga

[`Saga.cs`](src/Shop/After/Saga.cs) changes three things.

**The position is stored.** The order service writes down where each order is: `reserve`, `capture`, `confirm`, or,
going backwards, `refund`, `release`, `reject`. A process that starts up reads the unfinished ones and continues.

```csharp
case "capture":
    var (card, total) = await PaymentDetailsAsync(orderId);
    await faults.CallAsync("payments", "payments.capture", () => payments.CaptureAsync(orderId, card, total));
    await MoveAsync(orderId, "confirm");
    break;
```

**Every step can be repeated.** If a process dies between the call and `MoveAsync`, the next one makes the call again.
So the inventory service remembers reservations by order, and the payment service has a unique index on the order:

```sql
INSERT INTO payments.payments (order_id, amount, state) VALUES ($1, $2, 'captured') ON CONFLICT (order_id) DO NOTHING
```

**"No" and "no answer" are different.** A declined card is a definite no: the saga turns around and compensates. No
answer is not: the step may have happened. It is repeated, up to five times going forward, after which the order is
given up and undone. Undoing has no limit, because there is nothing to fall back to.

One consequence reaches the caller. The saga cannot always say confirmed or rejected at once. When a service does not
answer, the honest result is *pending*, and the client has to be able to live with it.

## All three under the same failures

One test sends the same 300 orders to each design while things go wrong at random: calls that do not arrive (4%),
calls that are carried out but answered too late (4%), releases and refunds that fail (10%), and the process dying
after reserving or after charging (2% each). Then every machine is healthy again and unfinished work is settled.
From CI (the run is seeded, so the numbers are the same every time):

| | Monolith | First attempt | Saga |
|---|---|---|---|
| Requests that ended in an error | @@E_M@@ | @@E_A@@ | @@E_S@@ |
| Confirmed / rejected | @@CR_M@@ | @@CR_A@@ | @@CR_S@@ |
| Orders left unfinished | 0 | @@U_A@@ | 0 |
| Units missing from stock | 0 | @@M_A@@ | 0 |
| Paid but not confirmed | 0 | @@P_A@@ | 0 |
| Charged twice | 0 | @@D_A@@ | 0 |

The monolith loses the orders during which the process died: they are rolled back, and the customer tries again.
The saga finishes them, which is why it confirms more.

## What it costs

- **More code and a table**, for something a database did for free. The saga here is about 150 lines for three steps.
- **Every operation of every participant has to be repeatable.** This is the larger part of the work, and it is in
  the other services, not in the saga.
- **A third answer.** "Pending" has to be handled by every caller and shown to every user.
- **No isolation.** If that matters for a step, it needs its own answer: a reservation that expires, a status others
  can see.
