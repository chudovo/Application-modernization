using System.Globalization;
using System.Net.Http.Json;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shipping.Api;
using Shipping.Contracts;
using Xunit;

namespace Shipping.ScenarioTests;

/// <summary>
/// Runs contract/scenarios.json against the migrated service twice: through the new REST API, and through the SOAP
/// endpoint with the same kind of WCF client the legacy consumers use. Each scenario gets its own application on its
/// own port, so scenarios run in parallel.
/// </summary>
public class ScenarioTests
{
    public static IEnumerable<object[]> Scenarios() => Scenario.Load().Select(s => new object[] { s!["name"]!.GetValue<string>() });

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Rest_api_honours_the_scenario(string name)
    {
        await using var app = await StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
        var scenario = Scenario.Find(name);
        Scenario.AssertOutcome(scenario["expect"]!.AsObject(), await RestClient.InvokeAsync(http, scenario));
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Soap_endpoint_honours_the_scenario(string name)
    {
        await using var app = await StartAsync();
        var factory = new ChannelFactory<IShipmentService>(new BasicHttpBinding(), new EndpointAddress(app.Urls.First() + "/ShipmentService.svc"));
        var channel = factory.CreateChannel();
        try
        {
            var scenario = Scenario.Find(name);
            Scenario.AssertOutcome(scenario["expect"]!.AsObject(), SoapClient.Invoke(channel, scenario));
        }
        finally
        {
            ((IClientChannel)channel).Abort();
            factory.Abort();
        }
    }

    // REST only: SOAP rejects an unknown enum value during deserialization, before the service is called.
    [Fact]
    public async Task Rest_api_answers_an_unbindable_body_with_a_problem_document()
    {
        await using var app = await StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

        using var response = await http.PutAsync("/api/shipments/SHP-000001/status", JsonContent.Create(new JsonObject { ["status"] = "Lost" }));
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        Assert.Equal(400, (int)response.StatusCode);
        Assert.Equal("bad_request", body["code"]!.GetValue<string>());
    }

    private static async Task<WebApplication> StartAsync()
    {
        var app = ShippingApp.Build(["--urls", "http://127.0.0.1:0"], services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedClock(new DateTimeOffset(2026, 2, 10, 12, 0, 0, TimeSpan.Zero)));
        });
        await app.StartAsync();
        return app;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

/// <summary>Calls the REST API and turns responses and ProblemDetails into the neutral outcome the scenarios describe.</summary>
public static class RestClient
{
    public static async Task<JsonObject> InvokeAsync(HttpClient http, JsonNode scenario)
    {
        var args = scenario["args"]!.AsObject();
        string? Arg(string key) => args[key]?.GetValue<string>();

        using var request = scenario["op"]!.GetValue<string>() switch
        {
            "GetShipment" => new HttpRequestMessage(HttpMethod.Get, $"/api/shipments/{Arg("trackingNumber")}"),
            "FindShipments" => new HttpRequestMessage(HttpMethod.Get, "/api/shipments?" + string.Join("&", args.Select(a => $"{a.Key}={a.Value}"))),
            "CreateShipment" => new HttpRequestMessage(HttpMethod.Post, "/api/shipments") { Content = JsonContent.Create(args) },
            "UpdateStatus" => new HttpRequestMessage(HttpMethod.Put, $"/api/shipments/{Arg("trackingNumber")}/status")
                { Content = JsonContent.Create(new JsonObject { ["status"] = Arg("newStatus") }) },
            "QuoteRate" => new HttpRequestMessage(HttpMethod.Post, "/api/rates/quote") { Content = JsonContent.Create(args) },
            var op => throw new InvalidOperationException($"Unknown operation {op}")
        };
        if (scenario["clientId"] is { } clientId) request.Headers.Add("X-Client-Id", clientId.GetValue<string>());

        using var response = await http.SendAsync(request);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        if (!response.IsSuccessStatusCode)
        {
            var kind = ((int)response.StatusCode, body["code"]?.GetValue<string>()) switch
            {
                (404, "shipment_not_found") => "NotFound",
                (400, "validation_failed") => "Validation",
                (409, "invalid_transition") => "InvalidTransition",
                var other => $"Unexpected {other}: {body.ToJsonString()}"
            };
            var error = new JsonObject { ["kind"] = kind };
            if (body["errors"] is { } errors) error["errors"] = errors.DeepClone();
            return new JsonObject { ["error"] = error };
        }

        return scenario["op"]!.GetValue<string>() switch
        {
            "FindShipments" => new JsonObject { ["trackingNumbers"] = new JsonArray([.. body.AsArray().Select(s => s!["trackingNumber"]!.DeepClone())]) },
            "QuoteRate" => new JsonObject { ["value"] = body["price"]!.DeepClone() },
            _ => new JsonObject { ["result"] = body }
        };
    }
}

/// <summary>Calls the SOAP contract with a WCF client and turns results and faults into the same neutral outcome.</summary>
public static class SoapClient
{
    public static JsonObject Invoke(IShipmentService channel, JsonNode scenario)
    {
        var args = scenario["args"]!.AsObject();
        string? Arg(string key) => args[key]?.GetValue<string>();
        T Enum<T>(string key) where T : struct => System.Enum.Parse<T>(Arg(key)!);

        using var scope = new OperationContextScope((IContextChannel)channel);
        if (scenario["clientId"] is { } clientId)
        {
            var http = new HttpRequestMessageProperty();
            http.Headers["X-Client-Id"] = clientId.GetValue<string>();
            OperationContext.Current.OutgoingMessageProperties[HttpRequestMessageProperty.Name] = http;
        }

        try
        {
            switch (scenario["op"]!.GetValue<string>())
            {
                case "GetShipment":
                    return Result(channel.GetShipment(Arg("trackingNumber")));
                case "FindShipments":
                    var query = new ShipmentQuery { CountryCode = Arg("countryCode"), Status = args["status"] is null ? null : Enum<ShipmentStatus>("status") };
                    return new JsonObject { ["trackingNumbers"] = new JsonArray([.. channel.FindShipments(query).Select(s => (JsonNode)s.TrackingNumber)]) };
                case "CreateShipment":
                    return Result(channel.CreateShipment(new CreateShipmentRequest
                    {
                        RecipientName = Arg("recipientName"), City = Arg("city"), PostalCode = Arg("postalCode"), CountryCode = Arg("countryCode"),
                        WeightKg = args["weightKg"]!.GetValue<decimal>(), Service = Enum<ServiceLevel>("service")
                    }));
                case "UpdateStatus":
                    return Result(channel.UpdateStatus(Arg("trackingNumber"), Enum<ShipmentStatus>("newStatus")));
                case "QuoteRate":
                    return new JsonObject { ["value"] = channel.QuoteRate(args["weightKg"]!.GetValue<decimal>(), Arg("countryCode"), Enum<ServiceLevel>("service")) };
                default:
                    throw new InvalidOperationException($"Unknown operation {scenario["op"]}");
            }
        }
        catch (FaultException<NotFoundFault>)
        {
            return Error("NotFound", null);
        }
        catch (FaultException<ValidationFault> fault)
        {
            return Error("Validation", fault.Detail.Errors);
        }
        catch (FaultException<InvalidTransitionFault>)
        {
            return Error("InvalidTransition", null);
        }
    }

    private static JsonObject Error(string kind, string[]? errors)
    {
        var error = new JsonObject { ["kind"] = kind };
        if (errors is not null) error["errors"] = new JsonArray([.. errors.Select(e => (JsonNode)e)]);
        return new JsonObject { ["error"] = error };
    }

    private static JsonObject Result(ShipmentData s) => new()
    {
        ["result"] = new JsonObject
        {
            ["trackingNumber"] = s.TrackingNumber, ["recipientName"] = s.RecipientName, ["city"] = s.City, ["postalCode"] = s.PostalCode,
            ["countryCode"] = s.CountryCode, ["weightKg"] = s.WeightKg, ["service"] = s.Service.ToString(), ["status"] = s.Status.ToString(),
            ["price"] = s.Price, ["createdBy"] = s.CreatedBy, ["createdUtc"] = Iso(s.CreatedUtc),
            ["deliveredUtc"] = s.DeliveredUtc is { } delivered ? Iso(delivered) : null
        }
    };

    private static string Iso(DateTime value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

public static class Scenario
{
    private static readonly Lazy<string> Json = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "contract", "scenarios.json"))) dir = dir.Parent;
        if (dir is null) throw new FileNotFoundException($"contract/scenarios.json not found above {AppContext.BaseDirectory}");
        return File.ReadAllText(Path.Combine(dir.FullName, "contract", "scenarios.json"));
    });

    public static JsonArray Load() => JsonNode.Parse(Json.Value)!.AsArray();

    public static JsonNode Find(string name) => Load().Single(s => s!["name"]!.GetValue<string>() == name)!;

    public static void AssertOutcome(JsonObject expect, JsonObject outcome)
    {
        var text = outcome.ToJsonString();
        if (expect["error"] is { } expectedError)
        {
            Assert.True(outcome["error"] is not null, $"Expected error {expectedError["kind"]} but got {text}");
            Assert.True(expectedError["kind"]!.GetValue<string>() == outcome["error"]!["kind"]!.GetValue<string>(), $"Expected error {expectedError["kind"]} but got {text}");
            if (expectedError["errors"] is { } errors)
                Assert.Equal(errors.AsArray().Select(e => e!.GetValue<string>()), (outcome["error"]!["errors"]?.AsArray() ?? []).Select(e => e!.GetValue<string>()));
            return;
        }

        Assert.True(outcome["error"] is null, $"Unexpected error: {text}");
        if (expect["trackingNumbers"] is { } numbers)
            Assert.Equal(numbers.AsArray().Select(e => e!.GetValue<string>()), outcome["trackingNumbers"]!.AsArray().Select(e => e!.GetValue<string>()));
        if (expect["value"] is { } value)
            Assert.True(value.GetValue<decimal>() == outcome["value"]!.GetValue<decimal>(), $"Expected {value} but got {outcome["value"]}");
        if (expect["result"] is { } result)
        {
            var actualResult = outcome["result"]!.AsObject();
            foreach (var (key, expected) in result.AsObject())
            {
                Assert.True(actualResult.ContainsKey(key), $"Missing '{key}' in {text}");
                var actual = actualResult[key];
                var same = expected?.GetValueKind() == System.Text.Json.JsonValueKind.Number
                    ? actual?.GetValueKind() == System.Text.Json.JsonValueKind.Number && expected.GetValue<decimal>() == actual.GetValue<decimal>()
                    : JsonNode.DeepEquals(expected, actual);
                Assert.True(same, $"{key}: expected {expected?.ToJsonString() ?? "null"} but got {actual?.ToJsonString() ?? "null"}");
            }
        }
    }
}
