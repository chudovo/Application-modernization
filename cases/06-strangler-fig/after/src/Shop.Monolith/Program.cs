using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shop.Monolith;

ShopMonolith.Build(args).Run();

public static class ShopMonolith
{
    /// <summary>Builds the application. Tests call this with their own settings and a fixed clock.</summary>
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);

        var database = new Database("shop");
        builder.Services.AddSingleton(database);
        builder.Services.AddDbContext<ShopContext>(o => o.UseSqlite(database.ConnectionString));
        builder.Services.TryAddSingleton(TimeProvider.System);

        // Catalog:Mode decides where the order code gets product data from. "Local" is the monolith's own table.
        if (builder.Configuration["Catalog:Mode"] == "Remote")
        {
            builder.Services.AddHttpClient<ICatalog, RemoteCatalog>(client =>
            {
                client.BaseAddress = new Uri(builder.Configuration["Catalog:Url"]!);
                client.Timeout = TimeSpan.FromSeconds(2);
                client.DefaultRequestHeaders.Add("X-Api-Key", builder.Configuration["ApiKey"] ?? "demo-key");
            });
        }
        else
        {
            builder.Services.AddScoped<ICatalog, LocalCatalog>();
        }
        // Named explicitly so that the controllers are found when a test project hosts the application.
        builder.Services.AddControllers().AddApplicationPart(typeof(ShopMonolith).Assembly)
            // Amounts always leave the shop with two decimals. Clients print them as they come.
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new MoneyJsonConverter()));

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShopContext>();
            db.Database.EnsureCreated();
            db.Seed();
        }

        // Behind the gateway the request arrives at an internal address. The gateway says which address the client
        // used (X-Forwarded-Host, X-Forwarded-Proto), and links such as the Location header are built from that.
        // By default only a proxy on the same machine is believed; in production, name the gateway's address here.
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto,
        });

        // Every request except the health check needs the API key.
        var apiKey = app.Configuration["ApiKey"] ?? "demo-key";
        app.Use(async (context, next) =>
        {
            if (context.Request.Path != "/api/health" && context.Request.Headers["X-Api-Key"] != apiKey)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "A valid X-Api-Key header is required." });
                return;
            }
            await next();
        });

        app.MapGet("/api/health", () => new { status = "ok" });
        app.MapControllers();
        return app;
    }
}

public sealed class MoneyJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value.ToString("0.00", CultureInfo.InvariantCulture));
}
