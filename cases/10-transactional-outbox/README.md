# Case 10: Transactional outbox, or how not to lose an event between a database and a broker

An order service that saves an order and tells a shipping service about it through a message broker. Before: two
separate writes, first the database, then the broker. After: the order and the event in one database transaction, a
relay that publishes from an outbox table, and a consumer that handles a message once however often it arrives.

Both designs pass the same scenarios when nothing goes wrong. The case is about what happens when something does.

| | Before | After |
|---|---|---|
| Order service | saves the order, then publishes the event | saves order and event in one transaction |
| Publishing | inside the request | by a relay, from the outbox table |
| Broker unavailable | the request fails, the order stays, no shipment ever | the request succeeds, the shipment follows later |
| Process dies between the two writes | event lost | nothing between them to die in |
| Delivery to the consumer | once, unless something fails | at least once |
| Consumer | creates a shipment per message | creates a shipment per message identity |

## Layout

```
database/schema.sql               tables of both services; the outbox and the processed-messages table are the additions
src/Shop/Before/DualWrite.cs      order service with two writes, consumer without deduplication
src/Shop/After/Outbox.cs          order service with outbox, relay, idempotent consumer
src/Shop/Common.cs                messages, RabbitMQ publisher and consumer, fault points
tests/Shop.Tests/                 scenarios for both designs, failure tests for each, one run under mixed failures
```

## Run it

You need the .NET 10 SDK, a PostgreSQL server and a RabbitMQ server. With Docker:

```bash
docker run -d -e POSTGRES_PASSWORD=local_dev_only -p 54320:5432 postgres:17
```

```bash
docker run -d -p 56720:5672 rabbitmq:4
```

```bash
dotnet test tests/Shop.Tests
```

Other servers: set `SHOP_PG` and `SHOP_AMQP`. The tests create and remove their own databases, exchanges and queues.

## What is verified, and what is not

| Check | Where | Result |
|---|---|---|
| 4 scenarios without failures, dual write | CI, PostgreSQL 17, RabbitMQ 4 | @@S1@@ |
| The same 4 scenarios, outbox | CI | @@S2@@ |
| 4 failure tests that show what dual write does | CI | @@F1@@ |
| 5 failure tests that show what the outbox guarantees | CI | @@F2@@ |
| 300 orders under mixed failures, both designs | CI | see below |

Not verified:

- **Real crashes.** A crash is simulated by an exception thrown at a named point, after which none of the following
  code runs. No process is killed, no connection is cut, no server is restarted.
- **A broker that is really down.** "Broker unavailable" is the publisher refusing at a fault point. Timeouts, half-open
  connections and a broker that accepts a message and loses it are not covered. The publisher does use confirms.
- **Ordering.** Events of different orders may overtake each other, more so with several relays. Nothing here needs an
  order, and nothing guarantees one.
- **Housekeeping and throughput.** Published outbox rows and processed message ids are never deleted here, and the
  relay polls. A production outbox needs cleanup, and often change data capture instead of polling.

## The problem in four tests

[`With_dual_write`](tests/Shop.Tests/FailureTests.cs) does not test that the old code works. It pins down what it does:

```csharp
await db.ExecuteAsync("INSERT INTO orders.orders ...");   // committed
// anything that goes wrong from here on leaves an order nobody is told about
await publisher.PublishAsync(Message.For(Guid.NewGuid(), new OrderPlaced(orderId, customer, total)));
```

- **Broker unavailable.** The caller gets an error. The order is stored. No shipment is ever created.
- **The customer retries.** They were told it failed, so they order again. Now there are two orders.
- **The process dies after the insert.** Same result as the first, without even an error to log.
- **A message is delivered twice** (the consumer did the work and died before acknowledging). Two shipments.

Publishing first and saving second does not help: then a failed insert leaves a shipment for an order that does not
exist.

## The outbox

```csharp
await using var transaction = await connection.BeginTransactionAsync();
// INSERT INTO orders.orders ...
// INSERT INTO orders.outbox (id, type, payload) ...
await transaction.CommitAsync();   // both or neither
```

The order service no longer talks to the broker at all. [`OutboxRelay`](src/Shop/After/Outbox.cs) reads unpublished
rows with `FOR UPDATE SKIP LOCKED`, publishes each one, and marks it published. If the relay dies after publishing
and before marking, the row is still unpublished and goes out again. That is deliberate: the choice is between
"at least once" and "maybe never".

So the consumer has to cope with repeats. The outbox row's id travels as the message id, and
[`IdempotentShippingHandler`](src/Shop/After/Outbox.cs) records it in the same transaction as the shipment:

```sql
INSERT INTO shipping.processed_messages (message_id) VALUES ($1) ON CONFLICT DO NOTHING;
-- 0 rows inserted: seen before, do nothing and acknowledge
```

What the five tests in `With_outbox` establish:

- Broker unavailable: the order is accepted without error, waits in the outbox, and is shipped when the broker returns.
- Process dies before the commit: neither order nor event exists.
- Relay dies after publishing: the message is published twice, delivered twice, shipped once.
- Consumer dies before acknowledging: delivered twice, shipped once.
- Three relays at once, 100 orders: 100 shipments, and exactly 100 messages published. `SKIP LOCKED` keeps the relays
  off each other's rows.

## Both designs under the same failures

One test sends 300 orders while four things go wrong at random: the broker refuses 15% of publishes, the process dies
after the insert in 3% of requests, the relay dies after publishing in 5%, the consumer dies before acknowledging in
5%. Then everything is healthy again and the system gets time to catch up. From one CI run (the numbers for dual write
vary a little from run to run, the ones for the outbox do not):

| | Dual write | Outbox |
|---|---|---|
| Requests that returned an error | @@E1@@ of 300 | 0 of 300 |
| Orders stored | 300 | 300 |
| Shipments | @@SH1@@ | 300 |
| Orders never shipped | @@N1@@ | 0 |
| Orders shipped more than once | @@D1@@ | 0 |

Every one of the requests that returned an error under dual write had stored its order.

## What it costs

- **Two more tables and a background process** that must run, be monitored, and be cleaned up after.
- **Delay.** A shipment appears after the relay's next round, not inside the request. Here that is tens of
  milliseconds; it is no longer zero.
- **Every consumer must be idempotent.** The outbox moves the problem of duplicates from "sometimes" to "by design".
  A consumer that cannot deduplicate turns a lost-message bug into a double-processing bug.
