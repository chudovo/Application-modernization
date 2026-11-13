using System;
using System.Collections.Generic;

namespace ParcelTracker.Models
{
    public class Shipment
    {
        public string TrackingNumber { get; set; }
        public string RecipientName { get; set; }
        public string City { get; set; }
        public string PostalCode { get; set; }
        public string CountryCode { get; set; }
        public decimal WeightKg { get; set; }
        public string Service { get; set; }
        public string Status { get; set; }
        public decimal Price { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? DeliveredUtc { get; set; }

        public string Title { get { return TrackingNumber + " · " + RecipientName; } }
        public string Subtitle { get { return City + ", " + CountryCode + " · " + StatusText.For(Status); } }
    }

    public class NewShipment
    {
        public string RecipientName { get; set; }
        public string City { get; set; }
        public string PostalCode { get; set; }
        public string CountryCode { get; set; }
        public decimal WeightKg { get; set; }
        public string Service { get; set; }
    }

    public static class StatusText
    {
        public static string For(string status)
        {
            return status == "InTransit" ? "In transit" : status;
        }
    }

    public class ApiException : Exception
    {
        public ApiException(string code, string message, IList<string> errors) : base(message)
        {
            Code = code;
            Errors = errors ?? new List<string>();
        }

        public string Code { get; private set; }
        public IList<string> Errors { get; private set; }
    }
}
