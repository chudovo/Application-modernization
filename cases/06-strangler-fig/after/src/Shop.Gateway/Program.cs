using Shop.Gateway;
using Yarp.ReverseProxy.Configuration;

ShopGateway.Build(args).Run();

namespace Shop.Gateway
{
    /// <summary>
    /// The facade. Clients talk only to this address. Which system answers a path is decided here and can be changed
    /// while the gateway is running.
    /// </summary>
    public static class ShopGateway
    {
        public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
        {
            var builder = WebApplication.CreateBuilder(args);
            configure?.Invoke(builder);

            var routing = new RoutingTable(
                monolithUrl: builder.Configuration["Monolith:Url"]!,
                catalogUrl: builder.Configuration["Catalog:Url"],
                catalogOwner: builder.Configuration["Routing:Catalog"] == "CatalogService" ? Owner.CatalogService : Owner.Monolith);
            builder.Services.AddSingleton(routing);
            builder.Services.AddSingleton<IProxyConfigProvider>(routing.Provider);
            builder.Services.AddReverseProxy();

            var app = builder.Build();
            app.MapReverseProxy();
            return app;
        }
    }

    public enum Owner { Monolith, CatalogService }

    public class RoutingTable
    {
        private readonly string _monolithUrl;
        private readonly string? _catalogUrl;

        public InMemoryConfigProvider Provider { get; }
        public Owner CatalogOwner { get; private set; }

        public RoutingTable(string monolithUrl, string? catalogUrl, Owner catalogOwner)
        {
            (_monolithUrl, _catalogUrl, CatalogOwner) = (monolithUrl, catalogUrl, catalogOwner);
            Provider = new InMemoryConfigProvider(Routes(), Clusters());
        }

        /// <summary>Moves the catalog paths to the other system. Takes effect for the next request, no restart.</summary>
        public void RouteCatalogTo(Owner owner)
        {
            CatalogOwner = owner;
            Provider.Update(Routes(), Clusters());
        }

        private RouteConfig[] Routes() =>
        [
            // The extracted slice: everything under /api/products.
            new RouteConfig
            {
                RouteId = "catalog",
                ClusterId = CatalogOwner == Owner.CatalogService ? "catalog-service" : "monolith",
                Match = new RouteMatch { Path = "/api/products/{**rest}" },
            },
            // Everything else still belongs to the monolith.
            new RouteConfig { RouteId = "everything-else", ClusterId = "monolith", Match = new RouteMatch { Path = "/{**rest}" } },
        ];

        private ClusterConfig[] Clusters()
        {
            var clusters = new List<ClusterConfig> { Cluster("monolith", _monolithUrl) };
            if (_catalogUrl is not null) clusters.Add(Cluster("catalog-service", _catalogUrl));
            return [.. clusters];
        }

        private static ClusterConfig Cluster(string id, string url) => new()
        {
            ClusterId = id,
            Destinations = new Dictionary<string, DestinationConfig> { ["only"] = new() { Address = url } },
        };
    }
}
