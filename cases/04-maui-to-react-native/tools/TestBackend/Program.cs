// A control server for the JavaScript tests. Each test asks for its own copy of the shipment service from case 02,
// on its own port and with a fixed clock, the same way the .NET test suites of case 03 start it in-process.
//
//   POST   /backends        -> { "id": "...", "url": "http://127.0.0.1:PORT" }
//   DELETE /backends/{id}   -> stops that copy
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shipping.Api;

var backends = new ConcurrentDictionary<string, WebApplication>();

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var control = builder.Build();

control.MapPost("/backends", async () =>
{
    var app = ShippingApp.Build(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default=Warning"], services =>
    {
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(new FixedClock(new DateTimeOffset(2026, 2, 10, 12, 0, 0, TimeSpan.Zero)));
    });
    await app.StartAsync();
    var id = Guid.NewGuid().ToString("N");
    backends[id] = app;
    return new { id, url = app.Urls.First() };
});

control.MapDelete("/backends/{id}", async (string id) =>
{
    if (backends.TryRemove(id, out var app)) await app.DisposeAsync();
    return Results.NoContent();
});

control.MapGet("/", () => "ready");

control.Run();

sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
