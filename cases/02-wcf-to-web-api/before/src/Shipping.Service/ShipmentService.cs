using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;
using Shipping.Contracts;

namespace Shipping.Service
{
    /// <summary>
    /// The whole application lives in the WCF service class: validation, pricing, the status rules,
    /// the mapping of errors to SOAP faults and the lookup of the caller in the transport headers.
    /// </summary>
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single, ConcurrencyMode = ConcurrencyMode.Single)]
    public class ShipmentService : IShipmentService
    {
        private static readonly HashSet<string> EuCountries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AT", "BE", "CZ", "DK", "ES", "FI", "FR", "IE", "IT", "LU", "NL", "PL", "PT", "SE"
        };

        public ShipmentData GetShipment(string trackingNumber)
        {
            var shipment = ShipmentStore.Find(trackingNumber);
            if (shipment == null) throw NotFound(trackingNumber);
            return shipment;
        }

        public ShipmentData[] FindShipments(ShipmentQuery query)
        {
            IEnumerable<ShipmentData> result = ShipmentStore.All();
            if (query != null && query.Status.HasValue) result = result.Where(s => s.Status == query.Status.Value);
            if (query != null && !string.IsNullOrEmpty(query.CountryCode))
                result = result.Where(s => string.Equals(s.CountryCode, query.CountryCode, StringComparison.OrdinalIgnoreCase));
            return result.ToArray();
        }

        public ShipmentData CreateShipment(CreateShipmentRequest request)
        {
            var errors = new List<string>();
            if (request == null || string.IsNullOrWhiteSpace(request.RecipientName)) errors.Add("RecipientName is required.");
            if (request == null || !IsCountryCode(request.CountryCode)) errors.Add("CountryCode must be a two-letter ISO code.");
            if (request == null || !IsWeight(request.WeightKg)) errors.Add("WeightKg must be greater than 0 and at most 30.");
            if (errors.Count > 0) throw Invalid(errors);

            return ShipmentStore.Add(new ShipmentData
            {
                RecipientName = request.RecipientName.Trim(),
                City = request.City,
                PostalCode = request.PostalCode,
                CountryCode = request.CountryCode.ToUpperInvariant(),
                WeightKg = request.WeightKg,
                Service = request.Service,
                Status = ShipmentStatus.Created,
                Price = Rate(request.WeightKg, request.CountryCode, request.Service),
                CreatedBy = CallerId(),
                CreatedUtc = SystemClock.UtcNow()
            });
        }

        public ShipmentData UpdateStatus(string trackingNumber, ShipmentStatus newStatus)
        {
            var shipment = ShipmentStore.Find(trackingNumber);
            if (shipment == null) throw NotFound(trackingNumber);

            var allowed =
                (shipment.Status == ShipmentStatus.Created && newStatus == ShipmentStatus.InTransit) ||
                (shipment.Status == ShipmentStatus.InTransit && (newStatus == ShipmentStatus.Delivered || newStatus == ShipmentStatus.Returned));
            if (!allowed)
                throw new FaultException<InvalidTransitionFault>(
                    new InvalidTransitionFault { From = shipment.Status, To = newStatus },
                    new FaultReason("Cannot change status from " + shipment.Status + " to " + newStatus + "."));

            shipment.Status = newStatus;
            if (newStatus == ShipmentStatus.Delivered) shipment.DeliveredUtc = SystemClock.UtcNow();
            return shipment;
        }

        public decimal QuoteRate(decimal weightKg, string countryCode, ServiceLevel service)
        {
            var errors = new List<string>();
            if (!IsCountryCode(countryCode)) errors.Add("CountryCode must be a two-letter ISO code.");
            if (!IsWeight(weightKg)) errors.Add("WeightKg must be greater than 0 and at most 30.");
            if (errors.Count > 0) throw Invalid(errors);
            return Rate(weightKg, countryCode, service);
        }

        private static decimal Rate(decimal weightKg, string countryCode, ServiceLevel service)
        {
            var basePrice = string.Equals(countryCode, "DE", StringComparison.OrdinalIgnoreCase) ? 4.90m
                : EuCountries.Contains(countryCode) ? 9.90m
                : 19.90m;
            var extraKilograms = Math.Max(0m, Math.Ceiling(weightKg - 1m));
            var price = basePrice + extraKilograms * 1.20m;
            if (service == ServiceLevel.Express) price *= 1.5m;
            return Math.Round(price, 2, MidpointRounding.AwayFromZero);
        }

        private static bool IsCountryCode(string value)
        {
            return value != null && value.Length == 2 && value.All(char.IsLetter);
        }

        private static bool IsWeight(decimal value)
        {
            return value > 0m && value <= 30m;
        }

        // The caller is identified by an HTTP header, read from the ambient WCF operation context.
        private static string CallerId()
        {
            object property;
            if (OperationContext.Current != null &&
                OperationContext.Current.IncomingMessageProperties.TryGetValue(HttpRequestMessageProperty.Name, out property))
            {
                var clientId = ((HttpRequestMessageProperty)property).Headers["X-Client-Id"];
                if (!string.IsNullOrEmpty(clientId)) return clientId;
            }
            return "unknown";
        }

        private static FaultException<NotFoundFault> NotFound(string trackingNumber)
        {
            return new FaultException<NotFoundFault>(new NotFoundFault { TrackingNumber = trackingNumber },
                new FaultReason("Shipment " + trackingNumber + " was not found."));
        }

        private static FaultException<ValidationFault> Invalid(List<string> errors)
        {
            return new FaultException<ValidationFault>(new ValidationFault { Errors = errors.ToArray() }, new FaultReason("The request is not valid."));
        }
    }
}
