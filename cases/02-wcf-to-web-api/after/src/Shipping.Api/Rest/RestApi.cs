using Microsoft.AspNetCore.Mvc;
using Shipping.Core;

namespace Shipping.Api.Rest;

public record ShipmentResponse(
    string TrackingNumber, string RecipientName, string? City, string? PostalCode, string CountryCode, decimal WeightKg,
    ServiceLevel Service, ShipmentStatus Status, decimal Price, string CreatedBy, DateTime CreatedUtc, DateTime? DeliveredUtc)
{
    public static ShipmentResponse From(Shipment s) => new(
        s.TrackingNumber, s.RecipientName, s.City, s.PostalCode, s.CountryCode, s.WeightKg, s.Service, s.Status, s.Price, s.CreatedBy, s.CreatedUtc, s.DeliveredUtc);
}

public record CreateShipmentBody(string? RecipientName, string? City, string? PostalCode, string? CountryCode, decimal WeightKg, ServiceLevel Service);

public record UpdateStatusBody(ShipmentStatus Status);

public record QuoteBody(decimal WeightKg, string? CountryCode, ServiceLevel Service);

public record QuoteResponse(decimal Price);

public static class RestApi
{
    public static void MapShippingApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        // GetShipment(trackingNumber) -> a resource addressed by its identifier.
        api.MapGet("/shipments/{trackingNumber}", (string trackingNumber, ShipmentManager shipments) =>
            ShipmentResponse.From(shipments.Get(trackingNumber)));

        // FindShipments(query) -> the query object becomes optional query-string parameters.
        api.MapGet("/shipments", (ShipmentStatus? status, string? countryCode, ShipmentManager shipments) =>
            shipments.Find(status, countryCode).Select(ShipmentResponse.From));

        // CreateShipment(request) -> POST to the collection, 201 with the address of the new resource.
        // The caller identity was read from the ambient OperationContext. Here it is an explicit parameter.
        api.MapPost("/shipments", (CreateShipmentBody body, [FromHeader(Name = "X-Client-Id")] string? clientId, ShipmentManager shipments) =>
        {
            var created = shipments.Create(new NewShipment(body.RecipientName, body.City, body.PostalCode, body.CountryCode, body.WeightKg, body.Service), clientId);
            return Results.Created($"/api/shipments/{created.TrackingNumber}", ShipmentResponse.From(created));
        });

        // UpdateStatus(trackingNumber, newStatus) -> PUT of the status sub-resource.
        api.MapPut("/shipments/{trackingNumber}/status", (string trackingNumber, UpdateStatusBody body, ShipmentManager shipments) =>
            ShipmentResponse.From(shipments.UpdateStatus(trackingNumber, body.Status)));

        // QuoteRate(...) -> a calculation with no resource behind it stays an RPC-style POST.
        api.MapPost("/rates/quote", (QuoteBody body, RateCalculator rates) =>
            new QuoteResponse(rates.Quote(body.WeightKg, body.CountryCode, body.Service)));
    }
}
