# Case 11: SQL Server to PostgreSQL, the differences that do not raise an error

The data access code of a small customer application, written for SQL Server, and the same code for PostgreSQL, with
the data copied across. Fourteen checks describe what the application does with its data. SQL Server defines the
expected results; PostgreSQL has to produce the same.

Most write-ups of this migration are tables of syntax: `TOP` becomes `LIMIT`, `GETDATE()` becomes `now()`. Syntax
errors are the easy part, because the database reports them. This case is about statements that run on both and
return something else.

| | Before | After |
|---|---|---|
| Database | SQL Server 2022 | PostgreSQL 17 |
| Driver | Microsoft.Data.SqlClient | Npgsql |
| String comparison | case-insensitive everywhere (server collation) | case-sensitive unless asked otherwise |
| Keys | `IDENTITY` | identity columns, sequences reset after the copy |
| Money | `money` | `numeric(19,4)` |
| "Insert, or update if it exists" | try, catch the duplicate, update | `INSERT ... ON CONFLICT` |

## Layout

```
database/sqlserver/                     schema and data as on SQL Server
database/postgres/schema.mechanical.sql the first translation of the schema, type for type
database/postgres/schema.sql            the schema after the checks
src/Crm/SqlServerStore.cs               before
src/Crm/MechanicalPostgresStore.cs      the first attempt: each statement translated to PostgreSQL syntax, nothing more
src/Crm/PostgresStore.cs                after; every changed statement says what SQL Server did without being asked
src/Crm/DataCopy.cs                     copies the rows and verifies them with counts and checksums
tests/Crm.Tests/                        14 behaviour checks per engine, 5 tests of the data move and its pitfalls
```

## Run it

You need the .NET 10 SDK and both databases. With Docker:

```bash
docker run -d -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=Local_dev_only_1 -p 14330:1433 mcr.microsoft.com/mssql/server:2022-latest
```

```bash
docker run -d -e POSTGRES_PASSWORD=local_dev_only -p 54320:5432 postgres:17
```

```bash
dotnet test tests/Crm.Tests
```

Other servers: set `CRM_SQLSERVER` and `CRM_PG`. The tests create and drop their own databases.

## What is verified, and what is not

| Check | Where | Result |
|---|---|---|
| 14 behaviour checks on SQL Server | CI, SQL Server 2022 | @@SS@@ |
| The same 14 on PostgreSQL, data copied from SQL Server | CI, PostgreSQL 17 | @@PG@@ |
| The same 14 on the mechanical translation | CI | @@MECH@@ |
| Data copy: row counts and checksums per table; pitfalls | CI | @@MIG@@ |

Not verified:

- **Stored procedures, triggers, views, functions.** This application keeps its SQL in the application. T-SQL code in
  the database is the larger part of most real migrations and is not touched here.
- **Volume.** Eleven rows are copied one at a time. A real copy uses bulk loading, runs for hours, and needs a plan
  for the changes that arrive meanwhile.
- **Performance.** No query plans were compared. Some of the fixes (`lower(name) = ...`, `ILIKE '%...%'`) need their
  own indexes on a real table.
- **Other collations.** SQL Server here uses the default case-insensitive collation and PostgreSQL the image's
  `en_US.utf8` locale. With other settings on either side, the list of differences changes.

## The first attempt

[`MechanicalPostgresStore`](src/Crm/MechanicalPostgresStore.cs) is the SQL Server code with every statement
translated: `+` to `||`, `OUTPUT INSERTED.id` to `RETURNING id`, `ISNULL` to `COALESCE`, `DATEDIFF` to `EXTRACT`,
`vip = 1` to `vip = true`. It compiles, and every statement is valid PostgreSQL. Against the checks:

| | Checks passing |
|---|---|
| SQL Server | 14 of 14 |
| Mechanical translation, data copied as it is | 4 of 14 |
| Mechanical translation, identity sequences reset after the copy | 5 of 14 |
| Finished version | 14 of 14 |

Only two of the failures are errors. The rest are wrong answers.

## What SQL Server did without being asked

| Check | SQL Server | Translated statement on PostgreSQL |
|---|---|---|
| Find by e-mail in another letter case | found | not found |
| Register a known address in another letter case | refused | accepted: a second account |
| Find by name in another case, with a trailing space | found | not found |
| Search names for `MEIER` | `bernd Meier` | nothing |
| Call list, never-ordered first | `NULL` sorts first | `NULL` sorts last |
| Average items per order, 13 / 5 | 2 | 3 |
| Days since an order at 23:30, asked at 00:30 | 1 | 0 |
| Insert, catch the duplicate, update, in one transaction | works | `current transaction is aborted` |
| First new order after the copy | id 6 | `duplicate key`, then a wrong day count |

**Letter case.** With the default collation SQL Server compares every string without regard to case, in `=`, in
`LIKE`, and in unique constraints. Nobody wrote that down, and the application depends on it in four places. In
PostgreSQL the e-mail column becomes `citext`, the name lookup uses `lower()`, and the search uses `ILIKE`.

**`citext` has a trap of its own**, found only because the finished version failed its first run:

```sql
SELECT ... FROM customers WHERE email = @p0            -- the parameter is text: compared as text, case-sensitive
SELECT ... FROM customers WHERE email = @p0::citext    -- compared as citext
```

A second one: Npgsql reads the list of types when a data source first connects. A data source that connected before
`CREATE EXTENSION citext` cannot read the column at all.

**NULLs sort at the other end.** `ORDER BY last_order_at` puts customers who never ordered first on SQL Server and
last on PostgreSQL. `NULLS FIRST` restores the order.

**The average of integers.** `AVG` of an `int` column is an `int` on SQL Server, cut off: 2. PostgreSQL returns 2.6,
which the application rounds to 3. The finished version computes what the dashboard has always shown. Whether 2 is
the right answer is a fair question, to be decided on purpose.

**Days are counted differently.** `DATEDIFF(day, a, b)` counts midnights between two moments. The usual translation,
`EXTRACT(DAY FROM b - a)`, counts whole 24-hour periods. An order at 23:30 is one day old an hour later by the first
and zero days old by the second. Subtracting dates (`b::date - a::date`) does what `DATEDIFF` did.

**A failed statement ends the transaction.** On SQL Server, "try the insert, catch the duplicate, update instead" works
inside a transaction. On PostgreSQL the failed insert aborts the transaction and every later statement is refused.
`INSERT ... ON CONFLICT (email) DO UPDATE` replaces the pattern.

**Identity sequences do not follow the data.** Rows are copied with their ids, and the sequences still start at 1.
The first insert collides with a copied row. The mechanical version made it worse by reporting that collision as
"a customer with this e-mail address already exists", because it treated every unique violation as a duplicate
address. That is also why one check passed for the wrong reason until the sequences were reset.
[`DataCopy`](src/Crm/DataCopy.cs) moves each sequence past the highest id.

## One difference in the other direction

`ISNULL(note, 'no note on file')` takes the type of its first argument, `nvarchar(10)`, and SQL Server has been
showing users `no note on`. `COALESCE` on PostgreSQL returns the whole text. This is recorded in a test of its own
instead of a shared check, because here the migration fixes something.

## Moving the data

[`DataCopy.VerifyAsync`](src/Crm/DataCopy.cs) reads every table from both databases and compares row counts and a
SHA-256 over all values, normalised so that the same value gives the same text from either driver (`bit` and
`boolean`, `money` and `numeric`, `datetime` and `timestamp`). A test changes one amount by a cent on the PostgreSQL
side and expects the verification to notice.
