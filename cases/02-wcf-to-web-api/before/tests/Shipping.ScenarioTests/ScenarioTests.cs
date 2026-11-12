using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.ServiceModel;
using System.ServiceModel.Channels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shipping.Contracts;
using Shipping.Service;
using Xunit;

// The service keeps its state in statics, so scenarios cannot run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Shipping.ScenarioTests
{
    /// <summary>
    /// Runs contract/scenarios.json against the legacy WCF service: a real ServiceHost with basicHttpBinding,
    /// called through a ChannelFactory client, the way existing .NET consumers call it.
    /// </summary>
    public class ScenarioTests
    {
        public static IEnumerable<object[]> Scenarios()
        {
            return Scenario.Load().Select(s => new object[] { (string)s["name"] });
        }

        [Theory]
        [MemberData(nameof(Scenarios))]
        public void Wcf_service_honours_the_scenario(string name)
        {
            var scenario = Scenario.Load().Single(s => (string)s["name"] == name);
            ShipmentStore.Reset();
            SystemClock.UtcNow = () => new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc);

            var address = "http://localhost:" + FreePort() + "/ShipmentService";
            using (var host = new ServiceHost(typeof(ShipmentService), new Uri(address)))
            {
                host.AddServiceEndpoint(typeof(IShipmentService), new BasicHttpBinding(), "");
                host.Open();

                var factory = new ChannelFactory<IShipmentService>(new BasicHttpBinding(), new EndpointAddress(address));
                var channel = factory.CreateChannel();
                try
                {
                    var outcome = SoapClient.Invoke(channel, scenario);
                    Scenario.AssertOutcome((JObject)scenario["expect"], outcome);
                }
                finally
                {
                    try { ((IClientChannel)channel).Close(); factory.Close(); }
                    catch (CommunicationException) { ((IClientChannel)channel).Abort(); factory.Abort(); }
                }
            }
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }

    /// <summary>Calls the SOAP contract and turns results and faults into the neutral outcome the scenarios describe.</summary>
    public static class SoapClient
    {
        public static JObject Invoke(IShipmentService channel, JToken scenario)
        {
            var args = (JObject)scenario["args"];
            using (new OperationContextScope((IContextChannel)channel))
            {
                var clientId = (string)scenario["clientId"];
                if (clientId != null)
                {
                    var http = new HttpRequestMessageProperty();
                    http.Headers["X-Client-Id"] = clientId;
                    OperationContext.Current.OutgoingMessageProperties[HttpRequestMessageProperty.Name] = http;
                }

                try
                {
                    switch ((string)scenario["op"])
                    {
                        case "GetShipment":
                            return Result(channel.GetShipment((string)args["trackingNumber"]));
                        case "FindShipments":
                            var query = new ShipmentQuery { CountryCode = (string)args["countryCode"] };
                            if (args["status"] != null) query.Status = Parse<ShipmentStatus>(args["status"]);
                            return new JObject { ["trackingNumbers"] = new JArray(channel.FindShipments(query).Select(s => s.TrackingNumber)) };
                        case "CreateShipment":
                            return Result(channel.CreateShipment(new CreateShipmentRequest
                            {
                                RecipientName = (string)args["recipientName"], City = (string)args["city"], PostalCode = (string)args["postalCode"],
                                CountryCode = (string)args["countryCode"], WeightKg = (decimal)args["weightKg"], Service = Parse<ServiceLevel>(args["service"])
                            }));
                        case "UpdateStatus":
                            return Result(channel.UpdateStatus((string)args["trackingNumber"], Parse<ShipmentStatus>(args["newStatus"])));
                        case "QuoteRate":
                            return new JObject { ["value"] = channel.QuoteRate((decimal)args["weightKg"], (string)args["countryCode"], Parse<ServiceLevel>(args["service"])) };
                        default:
                            throw new InvalidOperationException("Unknown operation " + scenario["op"]);
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
        }

        private static T Parse<T>(JToken value) where T : struct
        {
            return (T)Enum.Parse(typeof(T), (string)value);
        }

        private static JObject Error(string kind, string[] errors)
        {
            var error = new JObject { ["kind"] = kind };
            if (errors != null) error["errors"] = new JArray(errors);
            return new JObject { ["error"] = error };
        }

        private static JObject Result(ShipmentData s)
        {
            return new JObject
            {
                ["result"] = new JObject
                {
                    ["trackingNumber"] = s.TrackingNumber, ["recipientName"] = s.RecipientName, ["city"] = s.City, ["postalCode"] = s.PostalCode,
                    ["countryCode"] = s.CountryCode, ["weightKg"] = s.WeightKg, ["service"] = s.Service.ToString(), ["status"] = s.Status.ToString(),
                    ["price"] = s.Price, ["createdBy"] = s.CreatedBy, ["createdUtc"] = Iso(s.CreatedUtc),
                    ["deliveredUtc"] = s.DeliveredUtc.HasValue ? (JToken)Iso(s.DeliveredUtc.Value) : JValue.CreateNull()
                }
            };
        }

        private static string Iso(DateTime value)
        {
            return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }
    }

    public static class Scenario
    {
        public static JArray Load()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "contract", "scenarios.json"))) dir = dir.Parent;
            if (dir == null) throw new FileNotFoundException("contract/scenarios.json not found above " + AppDomain.CurrentDomain.BaseDirectory);
            using (var reader = new JsonTextReader(new StreamReader(Path.Combine(dir.FullName, "contract", "scenarios.json")))
                { FloatParseHandling = FloatParseHandling.Decimal, DateParseHandling = DateParseHandling.None })
                return JArray.Load(reader);
        }

        public static void AssertOutcome(JObject expect, JObject outcome)
        {
            var text = outcome.ToString(Formatting.None);
            if (expect["error"] != null)
            {
                Assert.True(outcome["error"] != null, "Expected error " + expect["error"]["kind"] + " but got " + text);
                Assert.True((string)expect["error"]["kind"] == (string)outcome["error"]["kind"], "Expected error " + expect["error"]["kind"] + " but got " + text);
                if (expect["error"]["errors"] != null)
                    Assert.Equal(expect["error"]["errors"].Select(e => (string)e).ToArray(), (outcome["error"]["errors"] ?? new JArray()).Select(e => (string)e).ToArray());
                return;
            }

            Assert.True(outcome["error"] == null, "Unexpected error: " + text);
            if (expect["trackingNumbers"] != null)
                Assert.Equal(expect["trackingNumbers"].Select(e => (string)e).ToArray(), outcome["trackingNumbers"].Select(e => (string)e).ToArray());
            if (expect["value"] != null)
                Assert.True(expect["value"].Value<decimal>() == outcome["value"].Value<decimal>(), "Expected " + expect["value"] + " but got " + outcome["value"]);
            if (expect["result"] != null)
            {
                foreach (var property in ((JObject)expect["result"]).Properties())
                {
                    var actual = outcome["result"][property.Name];
                    Assert.True(actual != null, "Missing '" + property.Name + "' in " + text);
                    var same = property.Value.Type == JTokenType.Integer || property.Value.Type == JTokenType.Float
                        ? (actual.Type == JTokenType.Integer || actual.Type == JTokenType.Float) && property.Value.Value<decimal>() == actual.Value<decimal>()
                        : JToken.DeepEquals(property.Value, actual);
                    Assert.True(same, property.Name + ": expected " + property.Value.ToString(Formatting.None) + " but got " + actual.ToString(Formatting.None));
                }
            }
        }
    }
}
