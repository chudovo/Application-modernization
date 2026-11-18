using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shop.Catalog;
using Shop.Gateway;

namespace Shop.Contract;

/// <summary>
/// Starts the pieces of the "after" state on free local ports, in the order a cutover would:
/// the catalog service (empty), the monolith, the copy of the catalog data, the gateway.
/// </summary>
public sealed class Topology : IRunningShop
{
    public HttpClient Client { get; private set; } = null!;
    public HttpClient MonolithDirect { get; private set; } = null!;
    public WebApplication Monolith { get; private set; } = null!;
    public WebApplication? Catalog { get; private set; }
    public WebApplication Gateway { get; private set; } = null!;
    public RoutingTable Routing => Gateway.Services.GetRequiredService<RoutingTable>();

    public static async Task<Topology> StartAsync(bool catalogExtracted)
    {
        var topology = new Topology();
        string? catalogUrl = null;

        if (catalogExtracted)
        {
            topology.Catalog = CatalogService.Build([], TestHost.OnFreePort);
            catalogUrl = await TestHost.StartAndGetAddressAsync(topology.Catalog);
        }

        topology.Monolith = ShopMonolith.Build([], builder =>
        {
            TestHost.OnFreePort(builder);
            builder.Services.AddSingleton<TimeProvider>(new FixedClock());
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Catalog:Mode"] = catalogExtracted ? "Remote" : "Local",
                ["Catalog:Url"] = catalogUrl,
            });
        });
        var monolithUrl = await TestHost.StartAndGetAddressAsync(topology.Monolith);
        topology.MonolithDirect = TestHost.ClientFor(monolithUrl);

        if (catalogExtracted)
        {
            using var scope = topology.Catalog!.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<Backfill>().RunAsync(monolithUrl, "demo-key");
        }

        topology.Gateway = ShopGateway.Build([], builder =>
        {
            TestHost.OnFreePort(builder);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Monolith:Url"] = monolithUrl,
                ["Catalog:Url"] = catalogUrl,
                ["Routing:Catalog"] = catalogExtracted ? "CatalogService" : "Monolith",
            });
        });
        topology.Client = TestHost.ClientFor(await TestHost.StartAndGetAddressAsync(topology.Gateway));
        return topology;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        MonolithDirect.Dispose();
        await Gateway.DisposeAsync();
        await Monolith.DisposeAsync();
        if (Catalog is not null) await Catalog.DisposeAsync();
    }
}

public class Through_the_gateway_with_everything_still_in_the_monolith : ContractScenarios
{
    protected override async Task<IRunningShop> StartAsync() => await Topology.StartAsync(catalogExtracted: false);
}

public class Through_the_gateway_with_the_catalog_extracted : ContractScenarios
{
    protected override async Task<IRunningShop> StartAsync() => await Topology.StartAsync(catalogExtracted: true);
}
