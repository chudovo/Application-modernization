using System.Collections.Concurrent;
using Xunit;
using Xunit.Abstractions;

namespace Payments.Tests;

/// <summary>The whole migration, start to finish, while the application is in use.</summary>
public class RolloutTests(ITestOutputHelper output)
{
    [Fact]
    public async Task The_whole_migration_runs_under_load_without_a_failed_operation_or_a_lost_cent()
    {
        await using var database = await TestDatabase.CreateAsync();

        // What is deployed right now. A rolling deployment replaces one instance at a time, so two releases overlap.
        IPaymentStore[] deployed = [database.Release(1), database.Release(1)];
        void Deploy(params int[] releases) => Volatile.Write(ref deployed, releases.Select(database.Release).ToArray());

        var failures = new ConcurrentBag<string>();
        var operations = 0;
        using var stop = new CancellationTokenSource();

        // Eight clients. Each has its own customer and keeps its own books, in decimals, of what the total must be.
        var clients = Enumerable.Range(0, 8).Select(n => Task.Run(async () =>
        {
            var customer = $"load-{n}";
            var random = new Random(n);
            var ledger = new Dictionary<long, decimal>();
            while (!stop.IsCancellationRequested)
            {
                var instances = Volatile.Read(ref deployed);
                var store = instances[random.Next(instances.Length)];
                var amount = random.Next(1, 100_000) / 100m;
                try
                {
                    if (ledger.Count > 0 && random.Next(4) == 0)
                    {
                        var id = ledger.Keys.ElementAt(random.Next(ledger.Count));
                        await store.AdjustAsync(id, amount);
                        ledger[id] = amount;
                    }
                    else
                    {
                        ledger[await store.RecordAsync(customer, amount)] = amount;
                    }

                    var total = await store.TotalForAsync(customer);
                    if (total != ledger.Values.Sum()) failures.Add($"release {store.Release}: total {total}, expected {ledger.Values.Sum()}");
                }
                catch (Exception e)
                {
                    failures.Add($"release {store.Release}: {e.Message.Split('\n')[0]}");
                }
                Interlocked.Increment(ref operations);
            }
            return (customer, expected: ledger.Values.Sum());
        })).ToArray();

        async Task Phase(string name, Func<Task> action)
        {
            var before = Volatile.Read(ref operations);
            await action();
            await Task.Delay(300);   // let the clients work in the new situation for a moment
            output.WriteLine($"ROLLOUT {name}: {Volatile.Read(ref operations) - before} operations, {failures.Count} failures so far");
        }

        var backfill = new Backfill(database.Db);

        await Phase("release 1 only, legacy schema", () => Task.CompletedTask);
        await Phase("step 1 expand", () => database.AdvanceToAsync(Schema.Expanded));
        await Phase("deploy: releases 1 and 2 side by side", () => { Deploy(1, 2); return Task.CompletedTask; });
        await Phase("deploy: release 2 only", () => { Deploy(2, 2); return Task.CompletedTask; });
        await Phase("step 2 backfill, small batches", async () => output.WriteLine($"ROLLOUT backfilled {await backfill.RunAsync(batchSize: 50)} rows"));

        Assert.Equal(0, await backfill.MismatchesAsync());   // the gate before reads move to the new column

        await Phase("step 3 constrain", () => database.AdvanceToAsync(Schema.Constrained));
        await Phase("deploy: releases 2 and 3 side by side", () => { Deploy(2, 3); return Task.CompletedTask; });
        await Phase("deploy: release 3 only", () => { Deploy(3, 3); return Task.CompletedTask; });
        await Phase("step 4 relax", () => database.AdvanceToAsync(Schema.Relaxed));
        await Phase("deploy: releases 3 and 4 side by side", () => { Deploy(3, 4); return Task.CompletedTask; });
        await Phase("deploy: release 4 only", () => { Deploy(4, 4); return Task.CompletedTask; });
        await Phase("step 5 contract", () => database.AdvanceToAsync(Schema.Contracted));

        stop.Cancel();
        var books = await Task.WhenAll(clients);
        output.WriteLine($"ROLLOUT total: {operations} operations, {failures.Count} failures");

        Assert.Empty(failures);
        Assert.True(operations > 500, $"only {operations} operations ran");

        // At the end the database agrees with every client's own books.
        var final = database.Release(4);
        foreach (var (customer, expected) in books) Assert.Equal(expected, await final.TotalForAsync(customer));

        // And the data from before the migration is intact.
        Assert.Equal(20.29m, await final.TotalForAsync("acme"));
        Assert.Equal("bigint", await database.ScalarAsync<string>(
            "SELECT string_agg(data_type, ',') FROM information_schema.columns WHERE table_name = 'payments' AND column_name LIKE 'amount%'"));
    }
}
