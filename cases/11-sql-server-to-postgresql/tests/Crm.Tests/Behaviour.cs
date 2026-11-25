namespace Crm.Tests;

/// <summary>
/// What the application does with its data, as named checks against <see cref="ICrmStore"/>. SQL Server defines the
/// expected results: this is what users have seen for years. Every check runs on a fresh copy of the data.
/// </summary>
public static class Behaviour
{
    private static readonly DateTime JustAfterMidnight = new(2026, 5, 11, 0, 30, 0);

    public static readonly (string Name, Func<ICrmStore, Task> Check)[] Checks =
    [
        ("a customer is found by e-mail address whatever the letter case", async store =>
        {
            Equal("Anna Schmidt", (await store.FindByEmailAsync("ANNA@Example.COM"))?.Name);
            Equal("bernd Meier", (await store.FindByEmailAsync("bernd.meier@example.com"))?.Name);
        }),
        ("a known e-mail address in another letter case is refused as a duplicate", async store =>
        {
            try { await store.RegisterAsync("Anna S.", "Anna@Example.com", "DE"); }
            catch (DuplicateEmailException) { return; }
            throw new InvalidOperationException("the duplicate was accepted");
        }),
        ("a name is found in another letter case and with a trailing space", async store =>
        {
            Equal("1", (await store.FindByNameAsync("anna schmidt ")).Count.ToString());
        }),
        ("the name search ignores letter case", async store =>
        {
            Equal("bernd Meier", string.Join(", ", await store.SearchNamesAsync("MEIER")));
        }),
        ("names are listed in the order users know", async store =>
        {
            Equal("Anna Schmidt, bernd Meier, carla Rossi, Émile Dubois, Österreich GmbH, Zoë Ärmel", string.Join(", ", await store.NamesInOrderAsync()));
        }),
        ("the call list starts with the customers who have never ordered", async store =>
        {
            Equal("Émile Dubois, Zoë Ärmel, Österreich GmbH, bernd Meier, carla Rossi, Anna Schmidt", string.Join(", ", await store.CallListAsync()));
        }),
        ("VIP customers are paged", async store =>
        {
            Equal("Anna Schmidt", string.Join(", ", await store.VipNamesAsync(page: 1, pageSize: 1)));
            Equal("carla Rossi", string.Join(", ", await store.VipNamesAsync(page: 2, pageSize: 1)));
        }),
        ("the average number of items per order is the whole number the dashboard shows", async store =>
        {
            // 2, 3, 1, 4 and 3 items: 13 / 5. The dashboard has always shown 2.
            Equal("2", (await store.AverageItemsPerOrderAsync()).ToString());
        }),
        ("days since the last order are calendar days", async store =>
        {
            // Ordered at 23:30, asked at 00:30: one hour later, and a day later.
            Equal("1", (await store.DaysSinceLastOrderAsync(1, JustAfterMidnight)).ToString());
            Equal("39", (await store.DaysSinceLastOrderAsync(2, JustAfterMidnight)).ToString());
        }),
        ("revenue of a region is summed to the cent", async store =>
        {
            Equal("588.89", (await store.RevenueAsync("DE")).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            Equal("0.00", (await store.RevenueAsync("FR")).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        }),
        ("register-or-update changes the name of a known customer and writes the audit line", async store =>
        {
            await store.RegisterOrUpdateAsync("Anna Schmidt-Lange", "anna@example.com", "DE", JustAfterMidnight);
            Equal("Anna Schmidt-Lange", (await store.FindByEmailAsync("anna@example.com"))?.Name);
            Equal("registered or updated anna@example.com", string.Join(", ", await store.AuditAsync()));
        }),
        ("register-or-update adds an unknown customer and writes the audit line", async store =>
        {
            await store.RegisterOrUpdateAsync("Neu Kunde", "neu@example.de", "DE", JustAfterMidnight);
            Equal("Neu Kunde", (await store.FindByEmailAsync("neu@example.de"))?.Name);
            Equal("1", (await store.AuditAsync()).Count.ToString());
        }),
        ("a new customer gets the next number", async store =>
        {
            Equal("7", (await store.RegisterAsync("Neu Kunde", "neu@example.de", "DE")).ToString());
        }),
        ("a new order gets the next number and counts as the last order", async store =>
        {
            Equal("6", (await store.PlaceOrderAsync(3, new DateTime(2026, 5, 10, 8, 0, 0), 2, 80.00m)).ToString());
            Equal("1", (await store.DaysSinceLastOrderAsync(3, JustAfterMidnight)).ToString());
            Equal("80.00", (await store.RevenueAsync("FR")).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        }),
    ];

    private static void Equal(string? expected, string? actual)
    {
        if (expected != actual) throw new InvalidOperationException($"expected '{expected}', got '{actual}'");
    }

    /// <summary>Runs every check against a fresh set of databases. Returns the checks that failed, with the reason.</summary>
    public static async Task<List<string>> FailuresAsync(Engine engine)
    {
        var failures = new List<string>();
        foreach (var (name, check) in Checks)
        {
            await using var databases = await TestDatabases.CreateAsync(engine);
            try { await check(databases.Store); }
            catch (Exception e) { failures.Add($"{name}: {e.Message.Split('\n')[0]}"); }
        }
        return failures;
    }
}
