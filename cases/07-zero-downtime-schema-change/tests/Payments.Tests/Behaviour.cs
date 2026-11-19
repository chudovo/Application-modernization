namespace Payments.Tests;

/// <summary>
/// What the application does with payments, as a list of named checks. The same checks are run for every release in
/// every schema state: together they are the definition of "nobody notices the migration".
/// </summary>
public static class Behaviour
{
    public static readonly (string Name, Func<IPaymentStore, Task> Check)[] Checks =
    [
        ("an existing payment is read", async store =>
        {
            Equal(new Payment(1, "acme", 19.99m), await store.GetAsync(1));
        }),
        ("an unknown payment is not found", async store =>
        {
            Equal(null, await store.GetAsync(999_999));
        }),
        ("an existing amount with more than two decimals is shown with two", async store =>
        {
            Equal(33.33m, (await store.GetAsync(4))!.Amount);
            Equal(-5.50m, (await store.GetAsync(8))!.Amount);
        }),
        ("a total over existing payments is exact to the cent", async store =>
        {
            // 19.99 + 0.1 + 0.2 as floating point numbers is 20.290000000000003.
            Equal(20.29m, await store.TotalForAsync("acme"));
        }),
        ("a new payment is stored and read back", async store =>
        {
            var id = await store.RecordAsync("initech", 0.10m);
            Equal(new Payment(id, "initech", 0.10m), await store.GetAsync(id));
        }),
        ("a refund is a negative payment and reduces the total", async store =>
        {
            await store.RecordAsync("umbrella", 120.00m);
            await store.RecordAsync("umbrella", -20.05m);
            Equal(99.95m, await store.TotalForAsync("umbrella"));
        }),
        ("an adjusted payment shows the new amount", async store =>
        {
            await store.AdjustAsync(2, 7.25m);
            Equal(7.25m, (await store.GetAsync(2))!.Amount);
            Equal(27.44m, await store.TotalForAsync("acme"));
        }),
        ("the most recent payments come newest first", async store =>
        {
            var first = await store.RecordAsync("hooli", 1.00m);
            var second = await store.RecordAsync("hooli", 2.00m);
            Equal($"{second}:2.00,{first}:1.00", string.Join(",", (await store.RecentAsync(2)).Select(p => $"{p.Id}:{p.Amount:0.00}")));
        }),
        ("a customer without payments has a total of zero", async store =>
        {
            Equal(0m, await store.TotalForAsync("nobody"));
        }),
    ];

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"expected {expected?.ToString() ?? "null"}, got {actual?.ToString() ?? "null"}");
    }

    /// <summary>Runs every check against a fresh database in the given state. Returns the checks that failed, with the reason.</summary>
    public static async Task<List<string>> FailuresAsync(int release, Schema state)
    {
        var failures = new List<string>();
        foreach (var (name, check) in Checks)
        {
            await using var database = await TestDatabase.CreateAsync(state);
            try { await check(database.Release(release)); }
            catch (Exception e) { failures.Add($"{name}: {e.Message.Split('\n')[0]}"); }
        }
        return failures;
    }
}
