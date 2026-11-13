// Shared by both test projects: starts the shipment service from case 02 on a free local port, with a fixed clock.
// The mobile app in both versions talks to it over real HTTP.
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shipping.Api;

namespace ParcelTracker.Scenarios;

public static class Backend
{
    public static async Task<WebApplication> StartAsync()
    {
        var app = ShippingApp.Build(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default=Warning"], services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedClock(new DateTimeOffset(2026, 2, 10, 12, 0, 0, TimeSpan.Zero)));
        });
        await app.StartAsync();
        return app;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
