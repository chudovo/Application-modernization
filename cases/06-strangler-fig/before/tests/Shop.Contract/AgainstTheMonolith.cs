using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Shop.Contract;

public class Against_the_monolith : ContractScenarios
{
    protected override async Task<IRunningShop> StartAsync()
    {
        var app = ShopMonolith.Build([], builder =>
        {
            TestHost.OnFreePort(builder);
            builder.Services.AddSingleton<TimeProvider>(new FixedClock());
        });
        return new Running(app, TestHost.ClientFor(await TestHost.StartAndGetAddressAsync(app)));
    }

    private sealed record Running(WebApplication App, HttpClient Client) : IRunningShop
    {
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
        }
    }
}
