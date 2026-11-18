using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Shop.Contract;

/// <summary>Every test sees the same moment, so that response bodies can be compared as text.</summary>
public sealed class FixedClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 4, 1, 8, 30, 0, TimeSpan.Zero);
}

public static class TestHost
{
    /// <summary>Common test settings: a free local port, quiet logs.</summary>
    public static void OnFreePort(WebApplicationBuilder builder)
    {
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
    }

    public static async Task<string> StartAndGetAddressAsync(WebApplication app)
    {
        await app.StartAsync();
        return app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    }

    public static HttpClient ClientFor(string address)
    {
        var client = new HttpClient { BaseAddress = new Uri(address) };
        client.DefaultRequestHeaders.Add("X-Api-Key", "demo-key");
        return client;
    }
}
