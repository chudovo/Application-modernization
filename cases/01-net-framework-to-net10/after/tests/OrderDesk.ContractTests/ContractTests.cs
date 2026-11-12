using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderDesk.Data;
using OrderDesk.Domain;
using Xunit;

namespace OrderDesk.ContractTests;

/// <summary>
/// Runs contract/cases.json against the migrated application: ASP.NET Core in memory, EF Core against SQLite.
/// The same file is run against the legacy application. No statics are involved, so tests run in parallel.
/// </summary>
public class ContractTests
{
    public static IEnumerable<object[]> Cases() => LoadCases().Select(c => new object[] { c!["name"]!.GetValue<string>() });

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Migrated_application_honours_the_contract(string name)
    {
        var contractCase = LoadCases().Single(c => c!["name"]!.GetValue<string>() == name)!;

        // One open in-memory SQLite connection is one private database for this test.
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ApiKey", "test-key");
            host.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<OrderDeskContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<OrderDeskContext>>();
                services.AddDbContext<OrderDeskContext>(o => o.UseSqlite(connection));
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedClock(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero)));
            });
        });

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrderDeskContext>();
            await db.Database.EnsureCreatedAsync();
            SeedData.Apply(db, scope.ServiceProvider.GetRequiredService<PricingService>());
        }

        using var client = factory.CreateClient();
        using var response = await client.SendAsync(BuildRequest(contractCase["request"]!));
        var body = await response.Content.ReadAsStringAsync();
        ContractAssert.Matches(contractCase["expect"]!, (int)response.StatusCode, response.Headers.Location, body);
    }

    private static HttpRequestMessage BuildRequest(JsonNode request)
    {
        var message = new HttpRequestMessage(new HttpMethod(request["method"]!.GetValue<string>()), request["path"]!.GetValue<string>());
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (request["apiKey"]?.GetValue<bool>() == true) message.Headers.Add("X-Api-Key", "test-key");

        if (request["body"] is { } body)
            message.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        else if (request["rawBody"] is { } raw)
            message.Content = new StringContent(raw.GetValue<string>(), Encoding.UTF8, "application/json");
        return message;
    }

    private static JsonArray LoadCases()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "contract", "cases.json"))) dir = dir.Parent;
        if (dir is null) throw new FileNotFoundException($"contract/cases.json not found above {AppContext.BaseDirectory}");
        return JsonNode.Parse(File.ReadAllText(Path.Combine(dir.FullName, "contract", "cases.json")))!.AsArray();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

public static class ContractAssert
{
    public static void Matches(JsonNode expect, int status, Uri? location, string body)
    {
        Assert.True(expect["status"]!.GetValue<int>() == status, $"Expected status {expect["status"]}, got {status}. Body: {body}");

        if (expect["emptyBody"]?.GetValue<bool>() == true) Assert.True(string.IsNullOrEmpty(body), $"Expected an empty body but got: {body}");
        if (expect["locationEndsWith"] is { } suffix)
            Assert.True(location is not null && location.ToString().EndsWith(suffix.GetValue<string>()), $"Location was {location}");

        if (expect["body"] is null && expect["bodyPartial"] is null && expect["bodyIds"] is null) return;

        var actual = JsonNode.Parse(body)!;
        if (expect["body"] is { } exact) AssertSame(exact, actual, "$", exactKeys: true);
        if (expect["bodyPartial"] is { } partial) AssertSame(partial, actual, "$", exactKeys: false);
        if (expect["bodyIds"] is { } ids)
            Assert.Equal(ids.AsArray().Select(i => i!.GetValue<int>()), actual.AsArray().Select(o => o!["Id"]!.GetValue<int>()));
    }

    // Property names are compared case-sensitively: "Id" and "id" are different contracts for a client.
    private static void AssertSame(JsonNode expected, JsonNode? actual, string path, bool exactKeys)
    {
        switch (expected.GetValueKind())
        {
            case JsonValueKind.Object:
                Assert.True(actual?.GetValueKind() == JsonValueKind.Object, $"{path}: expected an object, got {actual?.GetValueKind()}");
                var expectedObject = expected.AsObject();
                var actualObject = actual!.AsObject();
                foreach (var (key, value) in expectedObject)
                {
                    Assert.True(actualObject.ContainsKey(key), $"{path}: missing property '{key}'. Actual properties: {string.Join(", ", actualObject.Select(p => p.Key))}");
                    AssertSame(value!, actualObject[key], $"{path}.{key}", exactKeys);
                }
                if (exactKeys)
                {
                    var extra = actualObject.Select(p => p.Key).Except(expectedObject.Select(p => p.Key)).ToList();
                    Assert.True(extra.Count == 0, $"{path}: unexpected properties {string.Join(", ", extra)}");
                }
                break;
            case JsonValueKind.Array:
                Assert.True(actual?.GetValueKind() == JsonValueKind.Array, $"{path}: expected an array, got {actual?.GetValueKind()}");
                var expectedArray = expected.AsArray();
                var actualArray = actual!.AsArray();
                Assert.True(expectedArray.Count == actualArray.Count, $"{path}: expected {expectedArray.Count} items, got {actualArray.Count}");
                for (var i = 0; i < expectedArray.Count; i++) AssertSame(expectedArray[i]!, actualArray[i], $"{path}[{i}]", exactKeys);
                break;
            case JsonValueKind.Number:
                Assert.True(actual?.GetValueKind() == JsonValueKind.Number, $"{path}: expected a number, got {actual?.GetValueKind()} ({actual?.ToJsonString()})");
                // Compared as decimals so 49.90 and 49.9 are the same value.
                Assert.True(expected.GetValue<decimal>() == actual!.GetValue<decimal>(), $"{path}: expected {expected.ToJsonString()}, got {actual.ToJsonString()}");
                break;
            default:
                Assert.True(JsonNode.DeepEquals(expected, actual), $"{path}: expected {expected.ToJsonString()}, got {actual?.ToJsonString()}");
                break;
        }
    }
}
