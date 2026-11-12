using System;
using System.Runtime.Serialization;
using System.ServiceModel;

namespace Shipping.Contracts
{
    public static class Ns
    {
        public const string Service = "urn:shipping:v1";
    }

    [ServiceContract(Namespace = Ns.Service)]
    public interface IShipmentService
    {
        [OperationContract]
        [FaultContract(typeof(NotFoundFault))]
        ShipmentData GetShipment(string trackingNumber);

        [OperationContract]
        ShipmentData[] FindShipments(ShipmentQuery query);

        [OperationContract]
        [FaultContract(typeof(ValidationFault))]
        ShipmentData CreateShipment(CreateShipmentRequest request);

        [OperationContract]
        [FaultContract(typeof(NotFoundFault))]
        [FaultContract(typeof(InvalidTransitionFault))]
        ShipmentData UpdateStatus(string trackingNumber, ShipmentStatus newStatus);

        [OperationContract]
        [FaultContract(typeof(ValidationFault))]
        decimal QuoteRate(decimal weightKg, string countryCode, ServiceLevel service);
    }

    [DataContract(Namespace = Ns.Service)]
    public enum ShipmentStatus
    {
        [EnumMember] Created,
        [EnumMember] InTransit,
        [EnumMember] Delivered,
        [EnumMember] Returned
    }

    [DataContract(Namespace = Ns.Service)]
    public enum ServiceLevel
    {
        [EnumMember] Standard,
        [EnumMember] Express
    }

    [DataContract(Namespace = Ns.Service)]
    public class ShipmentData
    {
        [DataMember(Order = 1)] public string TrackingNumber { get; set; }
        [DataMember(Order = 2)] public string RecipientName { get; set; }
        [DataMember(Order = 3)] public string City { get; set; }
        [DataMember(Order = 4)] public string PostalCode { get; set; }
        [DataMember(Order = 5)] public string CountryCode { get; set; }
        [DataMember(Order = 6)] public decimal WeightKg { get; set; }
        [DataMember(Order = 7)] public ServiceLevel Service { get; set; }
        [DataMember(Order = 8)] public ShipmentStatus Status { get; set; }
        [DataMember(Order = 9)] public decimal Price { get; set; }
        [DataMember(Order = 10)] public string CreatedBy { get; set; }
        [DataMember(Order = 11)] public DateTime CreatedUtc { get; set; }

        // Left out of the XML entirely until the shipment is delivered.
        [DataMember(Order = 12, EmitDefaultValue = false)] public DateTime? DeliveredUtc { get; set; }
    }

    [DataContract(Namespace = Ns.Service)]
    public class ShipmentQuery
    {
        [DataMember(Order = 1, EmitDefaultValue = false)] public ShipmentStatus? Status { get; set; }
        [DataMember(Order = 2, EmitDefaultValue = false)] public string CountryCode { get; set; }
    }

    [DataContract(Namespace = Ns.Service)]
    public class CreateShipmentRequest
    {
        [DataMember(Order = 1, IsRequired = true)] public string RecipientName { get; set; }
        [DataMember(Order = 2)] public string City { get; set; }
        [DataMember(Order = 3)] public string PostalCode { get; set; }
        [DataMember(Order = 4, IsRequired = true)] public string CountryCode { get; set; }
        [DataMember(Order = 5, IsRequired = true)] public decimal WeightKg { get; set; }
        [DataMember(Order = 6)] public ServiceLevel Service { get; set; }
    }

    [DataContract(Namespace = Ns.Service)]
    public class NotFoundFault
    {
        [DataMember] public string TrackingNumber { get; set; }
    }

    [DataContract(Namespace = Ns.Service)]
    public class ValidationFault
    {
        [DataMember] public string[] Errors { get; set; }
    }

    [DataContract(Namespace = Ns.Service)]
    public class InvalidTransitionFault
    {
        [DataMember] public ShipmentStatus From { get; set; }
        [DataMember] public ShipmentStatus To { get; set; }
    }
}
