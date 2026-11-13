using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ParcelTracker.Models;

namespace ParcelTracker.Services;

/// <summary>The HttpClient is injected with its base address set; nothing is read from statics.</summary>
public class ShipmentApi(HttpClient http) : IShipmentApi
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<List<Shipment>> FindAsync(string? status) =>
        Send<List<Shipment>>(HttpMethod.Get, "/api/shipments" + (string.IsNullOrEmpty(status) ? "" : $"?status={Uri.EscapeDataString(status)}"));

    public Task<Shipment> GetAsync(string trackingNumber) =>
        Send<Shipment>(HttpMethod.Get, $"/api/shipments/{Uri.EscapeDataString(trackingNumber)}");

    public Task<Shipment> CreateAsync(NewShipment shipment, string? clientId) =>
        Send<Shipment>(HttpMethod.Post, "/api/shipments", shipment, clientId);

    public Task<Shipment> UpdateStatusAsync(string trackingNumber, string status) =>
        Send<Shipment>(HttpMethod.Put, $"/api/shipments/{Uri.EscapeDataString(trackingNumber)}/status", new { status });

    public async Task<decimal> QuoteAsync(decimal weightKg, string? countryCode, string service) =>
        (await Send<JsonObject>(HttpMethod.Post, "/api/rates/quote", new { weightKg, countryCode, service }))["price"]!.GetValue<decimal>();

    private async Task<T> Send<T>(HttpMethod method, string path, object? body = null, string? clientId = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        if (!string.IsNullOrEmpty(clientId)) request.Headers.Add("X-Client-Id", clientId);

        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var problem = string.IsNullOrEmpty(text) ? [] : JsonNode.Parse(text)!.AsObject();
            throw new ApiException(
                problem["code"]?.GetValue<string>() ?? $"http_{(int)response.StatusCode}",
                problem["title"]?.GetValue<string>() ?? "The request failed.",
                problem["errors"]?.AsArray().Select(e => e!.GetValue<string>()).ToList());
        }

        return JsonSerializer.Deserialize<T>(text, Json)!;
    }
}
