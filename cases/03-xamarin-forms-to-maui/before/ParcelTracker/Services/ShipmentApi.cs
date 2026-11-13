using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ParcelTracker.Models;

namespace ParcelTracker.Services
{
    public class ShipmentApi : IShipmentApi
    {
        // One static client for the whole app; the base address is read from a static on every call.
        private static readonly HttpClient Http = new HttpClient();

        public async Task<List<Shipment>> FindAsync(string status)
        {
            var url = "/api/shipments" + (string.IsNullOrEmpty(status) ? "" : "?status=" + Uri.EscapeDataString(status));
            return await Send<List<Shipment>>(HttpMethod.Get, url, null, null);
        }

        public Task<Shipment> GetAsync(string trackingNumber)
        {
            return Send<Shipment>(HttpMethod.Get, "/api/shipments/" + Uri.EscapeDataString(trackingNumber), null, null);
        }

        public Task<Shipment> CreateAsync(NewShipment shipment, string clientId)
        {
            return Send<Shipment>(HttpMethod.Post, "/api/shipments", shipment, clientId);
        }

        public Task<Shipment> UpdateStatusAsync(string trackingNumber, string status)
        {
            return Send<Shipment>(new HttpMethod("PUT"), "/api/shipments/" + Uri.EscapeDataString(trackingNumber) + "/status", new { status }, null);
        }

        public async Task<decimal> QuoteAsync(decimal weightKg, string countryCode, string service)
        {
            var quote = await Send<JObject>(HttpMethod.Post, "/api/rates/quote", new { weightKg, countryCode, service }, null);
            return quote.Value<decimal>("price");
        }

        private static async Task<T> Send<T>(HttpMethod method, string path, object body, string clientId)
        {
            var request = new HttpRequestMessage(method, AppConfig.ApiBaseUrl.TrimEnd('/') + path);
            if (body != null) request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(clientId)) request.Headers.Add("X-Client-Id", clientId);

            var response = await Http.SendAsync(request).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var settings = new JsonSerializerSettings { Culture = CultureInfo.InvariantCulture, DateTimeZoneHandling = DateTimeZoneHandling.Utc };

            if (!response.IsSuccessStatusCode)
            {
                var problem = string.IsNullOrEmpty(text) ? new JObject() : JObject.Parse(text);
                var errors = problem["errors"] == null ? null : problem["errors"].ToObject<List<string>>();
                throw new ApiException((string)problem["code"] ?? "http_" + (int)response.StatusCode, (string)problem["title"] ?? "The request failed.", errors);
            }

            return JsonConvert.DeserializeObject<T>(text, settings);
        }
    }
}
