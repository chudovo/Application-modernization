using System.Text.Json.Serialization;
using CoreWCF;
using CoreWCF.Configuration;
using Microsoft.AspNetCore.Routing;
using Shipping.Api.Rest;
using Shipping.Api.Soap;
using Shipping.Contracts;
using Shipping.Core;

namespace Shipping.Api;

public static class ShippingApp
{
    public static WebApplication Build(string[] args, Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        // The application itself: one store, one set of rules, an injected clock.
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ShipmentStore>();
        builder.Services.AddSingleton<RateCalculator>();
        builder.Services.AddSingleton<ShipmentManager>();

        // REST: enums as names, errors as ProblemDetails.
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<DomainExceptionHandler>();
        // A body that cannot be bound (unknown enum name, malformed JSON) would otherwise be answered with a bare 400 and
        // no body. Thrown as an exception, it goes through the same handler and gets a ProblemDetails body like every other error.
        builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

        // SOAP: the existing contract, hosted by CoreWCF for the clients that have not moved yet.
        builder.Services.AddServiceModelServices();
        builder.Services.AddTransient<SoapShipmentService>();

        configureServices?.Invoke(builder.Services);
        var app = builder.Build();

        app.UseExceptionHandler();
        app.MapShippingApi();
        app.UseServiceModel(soap =>
        {
            soap.AddService<SoapShipmentService>();
            soap.AddServiceEndpoint<SoapShipmentService, IShipmentService>(new BasicHttpBinding(), "/ShipmentService.svc");
        });

        return app;
    }
}
