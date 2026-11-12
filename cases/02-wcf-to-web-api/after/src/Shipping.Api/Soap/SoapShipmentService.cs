using CoreWCF;
using CoreWCF.Channels;
using Shipping.Contracts;
using Shipping.Core;
using ServiceLevel = Shipping.Contracts.ServiceLevel;
using ShipmentStatus = Shipping.Contracts.ShipmentStatus;

namespace Shipping.Api.Soap;

/// <summary>
/// Keeps the SOAP contract alive on .NET 10 through CoreWCF so that existing clients keep working while they are
/// moved to the REST API. It is an adapter and nothing else: map the request, call the core, map the result or the fault.
/// </summary>
public class SoapShipmentService(ShipmentManager shipments, RateCalculator rates) : IShipmentService
{
    public ShipmentData GetShipment(string trackingNumber) => Call(() => ToData(shipments.Get(trackingNumber)));

    public ShipmentData[] FindShipments(ShipmentQuery query) =>
        Call(() => shipments.Find((Core.ShipmentStatus?)query?.Status, query?.CountryCode).Select(ToData).ToArray());

    public ShipmentData CreateShipment(CreateShipmentRequest request) => Call(() =>
    {
        var input = request is null ? null
            : new NewShipment(request.RecipientName, request.City, request.PostalCode, request.CountryCode, request.WeightKg, (Core.ServiceLevel)request.Service);
        return ToData(shipments.Create(input, CallerId()));
    });

    public ShipmentData UpdateStatus(string trackingNumber, ShipmentStatus newStatus) =>
        Call(() => ToData(shipments.UpdateStatus(trackingNumber, (Core.ShipmentStatus)newStatus)));

    public decimal QuoteRate(decimal weightKg, string countryCode, ServiceLevel service) =>
        Call(() => rates.Quote(weightKg, countryCode, (Core.ServiceLevel)service));

    // Domain exceptions become the same typed faults the legacy service declared in its contract.
    private static T Call<T>(Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (ShipmentNotFoundException e)
        {
            throw new FaultException<NotFoundFault>(new NotFoundFault { TrackingNumber = e.TrackingNumber }, new FaultReason(e.Message));
        }
        catch (ValidationFailedException e)
        {
            throw new FaultException<ValidationFault>(new ValidationFault { Errors = [.. e.Errors] }, new FaultReason(e.Message));
        }
        catch (InvalidTransitionException e)
        {
            throw new FaultException<InvalidTransitionFault>(
                new InvalidTransitionFault { From = (ShipmentStatus)e.From, To = (ShipmentStatus)e.To }, new FaultReason(e.Message));
        }
    }

    private static string? CallerId() =>
        OperationContext.Current?.IncomingMessageProperties.TryGetValue(HttpRequestMessageProperty.Name, out var property) == true
            ? ((HttpRequestMessageProperty)property).Headers["X-Client-Id"]
            : null;

    private static ShipmentData ToData(Shipment s) => new()
    {
        TrackingNumber = s.TrackingNumber, RecipientName = s.RecipientName, City = s.City, PostalCode = s.PostalCode, CountryCode = s.CountryCode,
        WeightKg = s.WeightKg, Service = (ServiceLevel)s.Service, Status = (ShipmentStatus)s.Status, Price = s.Price, CreatedBy = s.CreatedBy,
        CreatedUtc = s.CreatedUtc, DeliveredUtc = s.DeliveredUtc
    };
}
