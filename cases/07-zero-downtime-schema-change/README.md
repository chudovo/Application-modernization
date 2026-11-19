# Case 07: Changing a column without downtime (expand and contract)

A payments table that stores money as a floating point number, and the same table storing whole cents as an integer.
The change is made while the application keeps running and while old and new releases of it run side by side.

The table changes in five steps, the application in four releases. A test runs the whole sequence under load and
counts failed operations; another runs every release against every schema state.

| | Before | After |
|---|---|---|
| Column | `amount double precision` | `amount_cents bigint` |
| Application | Release 1 | Release 4 |
| `19.99 + 0.1 + 0.2` in the database | 20.290000000000003 | 2029 |
| Database | PostgreSQL 17 | PostgreSQL 17 |

## Layout

```
database/legacy-schema.sql    the table before the change
database/legacy-data.sql      rows as the old application left them
database/steps/               1-expand, 3-constrain, 4-relax, 5-contract (step 2 is the backfill, in code)
src/Payments/Releases/        the four releases of the data access code, one class each
src/Payments/Migration.cs     Migrator (lock timeout and retry) and Backfill (batches, verification)
tests/Payments.Tests/         behaviour checks, compatibility matrix, rollout under load, pitfalls
```

There is no `before/` and `after/` folder in this case: the point is the path between them. Before is Release 1 on
the legacy schema, after is Release 4 on the contracted schema.

## Run it

You need the .NET 10 SDK and a PostgreSQL server. With Docker:

```bash
docker run -d -e POSTGRES_PASSWORD=local_dev_only -p 54320:5432 postgres:17
dotnet test tests/Payments.Tests
```

To use another server, set `PAYMENTS_PG` to its connection string. The tests create and drop their own databases.

## The sequence

| | Database | Application |
|---|---|---|
| 1 | **Expand**: add `amount_cents`, nullable | Release 1 keeps running |
| | | deploy **Release 2**: writes both columns, reads the old one |
| 2 | **Backfill**: make `amount_cents` agree with `amount`, in small batches | Release 2 only |
| | Gate: the verification query returns 0 | |
| 3 | **Constrain**: `amount_cents` becomes `NOT NULL` | |
| | | deploy **Release 3**: reads the new column, still writes both |
| 4 | **Relax**: `amount` loses `NOT NULL` | |
| | | deploy **Release 4**: reads and writes only the new column |
| 5 | **Contract**: drop `amount` | Release 4 only |

## What is verified, and what is not

| Check | Where | Result |
|---|---|---|
| 9 behaviour checks for each of the 20 combinations of release and schema state | CI, PostgreSQL 17 | matrix below, as expected |
| The whole sequence under load from 8 clients, with rolling deployments | CI | 0 failed operations, books agree |
| Lock queue, lock timeout, backfill restart, overlap of releases, changed sums | CI | 6 / 6 |

Not verified:

- **A large table.** The backfill handles a few hundred to a few thousand rows here. Batch size, replication lag,
  vacuum and index bloat on a table with hundreds of millions of rows are not explored.
- **Other databases.** Everything about locks is specific to PostgreSQL. MySQL and SQL Server behave differently
  for the same statements.
- **Other readers of the table.** Reports, exports and replicas that read `amount` directly are part of a real
  migration and not part of this one.
- **A real deployment.** "Deploying a release" in the test means swapping which class handles requests. In-flight
  requests, connection pools and deployment tooling are not involved.

## The compatibility matrix

Number of the 9 behaviour checks that pass, measured by
[`CompatibilityTests.cs`](tests/Payments.Tests/CompatibilityTests.cs):

| | Legacy | 1 Expanded | 3 Constrained | 4 Relaxed | 5 Contracted |
|---|---|---|---|---|---|
| Release 1 | **9** | **9** | 6 | 6 | 0 |
| Release 2 | 5 | **9** | **9** | **9** | 0 |
| Release 3 | 0 | 5 | **9** | **9** | 5 |
| Release 4 | 0 | 2 | 6 | **9** | **9** |

Read along a row: a release works in a band of schema states. Read down a column: in every state between the first
and the last at least two releases work, which is what makes a rolling deployment possible. The rules of the sequence follow from the cells
that are not 9:

- Release 1 in *Constrained*: `null value in column "amount_cents" violates not-null constraint`. No Release 1
  instance may be left when step 3 runs.
- Release 3 in *Expanded*: `Column 'amount_cents' is null`. Reads may move only after the backfill.
- Release 4 in *Constrained*: `null value in column "amount" violates not-null constraint`. This is why step 4
  exists. It is easy to forget, because nothing about the new column needs it.
- Release 3 in *Contracted*: `column "amount" does not exist`. No Release 3 instance may be left when step 5 runs.

## The rollout under load

[`RolloutTests.cs`](tests/Payments.Tests/RolloutTests.cs) starts eight clients that record payments, adjust them and
read totals without pause. Each keeps its own books in decimals. While they run, the test walks through the whole
sequence, including the three phases in which two releases are deployed at once. At the end it requires zero failed
operations, and that the database agrees with every client's books to the cent. In CI: 5,579 operations, 0 failed, 592 rows backfilled while the clients were writing (the counts vary from run to run).

### What it found

The first version of the backfill filled the rows where `amount_cents` was empty. Under load the gate before step 3
reported 4 rows that disagreed. While Release 1 and Release 2 run side by side, Release 2 records a payment with both
columns, and Release 1 then adjusts the amount, updating only the column it knows. That row is not empty. It is
wrong, and a backfill that looks for `NULL` never touches it.

```sql
-- finds nothing to do, although the row says 12.00 in one column and 10.00 in the other
UPDATE payments SET amount_cents = round(amount * 100)::bigint WHERE amount_cents IS NULL;

-- the backfill of this case: compare instead
... WHERE amount_cents IS DISTINCT FROM round(amount * 100)::bigint
```

Until reads move to the new column, the old one is the truth, so recomputing from it is always right. The same
comparison is the verification query, and the sequence does not continue until it returns zero.

## Locks: how a one-millisecond change takes the application down

`ALTER TABLE` needs an exclusive lock. If any transaction that has touched the table is still open, the `ALTER` waits,
and every query that arrives after it waits behind it. A forgotten session is enough to make the table unusable.
[`PitfallTests.cs`](tests/Payments.Tests/PitfallTests.cs) reproduces it: with one idle transaction open, a plain
`SELECT` from the application times out behind the waiting schema change.

[`Migrator`](src/Payments/Migration.cs) sets `lock_timeout` and retries. The schema change gives up after 100 ms, the
queue dissolves, and the application keeps reading while the migration tries again until the long transaction ends.

Step 3 has a second lock problem. `SET NOT NULL` on its own scans the whole table under the exclusive lock.
[`3-constrain.sql`](database/steps/3-constrain.sql) adds a `CHECK` constraint as `NOT VALID`, validates it without
blocking writes, and only then sets `NOT NULL`, which PostgreSQL 12 and later accepts without another scan.

## One thing that does change

Three existing payments are 100 / 3 each. Release 1 adds the floating point numbers and rounds the sum: 100.00.
After the migration each row is 3333 cents and the sum is 99.99. Both are correct by their own definition. Amounts
that were never whole cents cannot be converted without choosing one, and the choice shows up in totals. This is
asserted by a test, and it is a question for whoever owns the numbers, before step 2 and not after step 5.

## Going back

Up to and including Release 3 the old column is complete and current, so any earlier release can be deployed again.
Release 4 stops writing it. From that moment, going back to Release 2 or 1 needs the old column to be filled from the
new one first. After step 5 there is nothing to go back to.
