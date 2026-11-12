using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Owin.Testing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OrderDesk.Api;
using OrderDesk.Api.Infrastructure;
using OrderDesk.Data;
using OrderDesk.Domain;
using Xunit;

// The application keeps its state in statics (ServiceLocator, Clock), so tests cannot run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace OrderDesk.ContractTests
{
    /// <summary>
    /// Runs contract/cases.json against the legacy application: Web API 2 hosted in memory through OWIN,
    /// Entity Framework 6 against SQL Server LocalDB. The same file is run against the migrated application.
    /// </summary>
    public class ContractTests
    {
        public static IEnumerable<object[]> Cases()
        {
            return LoadCases().Select(c => new object[] { (string)c["name"] });
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Legacy_application_honours_the_contract(string name)
        {
            var contractCase = LoadCases().Single(c => (string)c["name"] == name);
            var connectionString = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Connect Timeout=60;Database=OrderDeskContract_" + Guid.NewGuid().ToString("N");

            Database.SetInitializer<OrderDeskContext>(null);
            using (var db = new OrderDeskContext(connectionString))
            {
                db.Database.Create();
                SeedData.Apply(db);
            }

            try
            {
                Clock.UtcNow = () => new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
                ServiceLocator.Register(() => new OrderService(() => new OrderDeskContext(connectionString)));

                using (var server = TestServer.Create<Startup>())
                using (var response = await server.HttpClient.SendAsync(BuildRequest(contractCase["request"])))
                {
                    var body = await response.Content.ReadAsStringAsync();
                    ContractAssert.Matches(contractCase["expect"], (int)response.StatusCode, response.Headers.Location, body);
                }
            }
            finally
            {
                using (var db = new OrderDeskContext(connectionString)) db.Database.Delete();
            }
        }

        private static HttpRequestMessage BuildRequest(JToken request)
        {
            var message = new HttpRequestMessage(new HttpMethod((string)request["method"]), (string)request["path"]);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if ((bool?)request["apiKey"] == true) message.Headers.Add("X-Api-Key", "test-key");

            if (request["body"] != null)
                message.Content = new StringContent(request["body"].ToString(Formatting.None), Encoding.UTF8, "application/json");
            else if (request["rawBody"] != null)
                message.Content = new StringContent((string)request["rawBody"], Encoding.UTF8, "application/json");
            return message;
        }

        private static JArray LoadCases()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "contract", "cases.json"))) dir = dir.Parent;
            if (dir == null) throw new FileNotFoundException("contract/cases.json not found above " + AppDomain.CurrentDomain.BaseDirectory);
            // Keep numbers as decimals so 49.90 and 49.9 compare as equal values, not as text.
            using (var reader = new JsonTextReader(new StreamReader(Path.Combine(dir.FullName, "contract", "cases.json"))) { FloatParseHandling = FloatParseHandling.Decimal })
                return JArray.Load(reader);
        }
    }

    public static class ContractAssert
    {
        public static void Matches(JToken expect, int status, Uri location, string body)
        {
            Assert.Equal((int)expect["status"], status);

            if ((bool?)expect["emptyBody"] == true) Assert.True(string.IsNullOrEmpty(body), "Expected an empty body but got: " + body);
            if (expect["locationEndsWith"] != null)
                Assert.True(location != null && location.ToString().EndsWith((string)expect["locationEndsWith"]), "Location was " + location);

            if (expect["body"] == null && expect["bodyPartial"] == null && expect["bodyIds"] == null) return;

            JToken actual;
            using (var reader = new JsonTextReader(new StringReader(body)) { FloatParseHandling = FloatParseHandling.Decimal, DateParseHandling = DateParseHandling.None })
                actual = JToken.Load(reader);

            if (expect["body"] != null) AssertSame(expect["body"], actual, "$", exactKeys: true);
            if (expect["bodyPartial"] != null) AssertSame(expect["bodyPartial"], actual, "$", exactKeys: false);
            if (expect["bodyIds"] != null)
                Assert.Equal(expect["bodyIds"].Select(t => (int)t).ToArray(), actual.Select(t => (int)t["Id"]).ToArray());
        }

        // Property names are compared case-sensitively: "Id" and "id" are different contracts for a client.
        private static void AssertSame(JToken expected, JToken actual, string path, bool exactKeys)
        {
            switch (expected.Type)
            {
                case JTokenType.Object:
                    Assert.True(actual.Type == JTokenType.Object, path + ": expected an object, got " + actual.Type);
                    var expectedObject = (JObject)expected;
                    var actualObject = (JObject)actual;
                    foreach (var property in expectedObject.Properties())
                    {
                        Assert.True(actualObject.Property(property.Name, StringComparison.Ordinal) != null,
                            path + ": missing property '" + property.Name + "'. Actual properties: " + string.Join(", ", actualObject.Properties().Select(p => p.Name)));
                        AssertSame(property.Value, actualObject[property.Name], path + "." + property.Name, exactKeys);
                    }
                    if (exactKeys)
                    {
                        var extra = actualObject.Properties().Select(p => p.Name).Except(expectedObject.Properties().Select(p => p.Name)).ToList();
                        Assert.True(extra.Count == 0, path + ": unexpected properties " + string.Join(", ", extra));
                    }
                    break;
                case JTokenType.Array:
                    Assert.True(actual.Type == JTokenType.Array, path + ": expected an array, got " + actual.Type);
                    Assert.True(expected.Count() == actual.Count(), path + ": expected " + expected.Count() + " items, got " + actual.Count());
                    for (var i = 0; i < expected.Count(); i++) AssertSame(expected[i], actual[i], path + "[" + i + "]", exactKeys);
                    break;
                case JTokenType.Integer:
                case JTokenType.Float:
                    Assert.True(actual.Type == JTokenType.Integer || actual.Type == JTokenType.Float, path + ": expected a number, got " + actual.Type + " (" + actual + ")");
                    Assert.True(expected.Value<decimal>() == actual.Value<decimal>(), path + ": expected " + expected + ", got " + actual);
                    break;
                default:
                    Assert.True(JToken.DeepEquals(expected, actual), path + ": expected " + expected.ToString(Formatting.None) + ", got " + actual.ToString(Formatting.None));
                    break;
            }
        }
    }
}
