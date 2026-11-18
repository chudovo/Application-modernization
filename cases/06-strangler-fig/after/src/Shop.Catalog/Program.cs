using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shop.Catalog;

CatalogService.Build(args).Run();

namespace Shop.Catalog
{
    public class Product
    {
        public required string Sku { get; set; }
        public required string Name { get; set; }
        public decimal Price { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>The catalog's own database. Nothing else reads it.</summary>
    public class CatalogContext(DbContextOptions<CatalogContext> options) : DbContext(options)
    {
        public DbSet<Product> Products => Set<Product>();
        protected override void OnModelCreating(ModelBuilder model) => model.Entity<Product>().HasKey(p => p.Sku);
    }

    public record NewProduct(string? Sku, string? Name, decimal Price);
    public record PriceChange(decimal Price);

    public static class CatalogService
    {
        public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
        {
            var builder = WebApplication.CreateBuilder(args);
            configure?.Invoke(builder);

            var connectionString = $"Data Source=file:catalog-{Guid.NewGuid():N}?mode=memory&cache=shared";
            var keepAlive = new SqliteConnection(connectionString);
            keepAlive.Open();
            builder.Services.AddSingleton(keepAlive);
            builder.Services.AddDbContext<CatalogContext>(o => o.UseSqlite(connectionString));
            builder.Services.AddHttpClient<Backfill>();

            // Contract: amounts are written with two decimals, as the monolith writes them.
            builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new MoneyJsonConverter()));

            var app = builder.Build();
            using (var scope = app.Services.CreateScope())
                scope.ServiceProvider.GetRequiredService<CatalogContext>().Database.EnsureCreated();

            // Contract: links in responses use the address the client talked to, which only the gateway knows.
            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto,
            });

            var apiKey = app.Configuration["ApiKey"] ?? "demo-key";
            app.Use(async (context, next) =>
            {
                if (context.Request.Path != "/api/health" && context.Request.Headers["X-Api-Key"] != apiKey)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(Error("A valid X-Api-Key header is required."));
                    return;
                }
                await next();
            });

            app.MapGet("/api/health", () => new { status = "ok" });

            var products = app.MapGroup("/api/products");

            // Contract: sorted by SKU, and the search ignores case (LIKE does, a plain Contains would not).
            products.MapGet("", async (string? search, CatalogContext db) =>
                await db.Products
                    .Where(p => search == null || EF.Functions.Like(p.Name, $"%{search}%"))
                    .OrderBy(p => p.Sku).ToListAsync());

            // Contract: every error has a body of the form { "error": "..." }. Clients show that text.
            products.MapGet("{sku}", async (string sku, CatalogContext db) =>
                await db.Products.FindAsync(sku) is { } product ? Results.Ok(product) : Results.NotFound(Error($"Product {sku} was not found.")));

            products.MapPost("", async (NewProduct request, CatalogContext db, HttpContext http) =>
            {
                if (string.IsNullOrWhiteSpace(request.Sku) || string.IsNullOrWhiteSpace(request.Name))
                    return Results.BadRequest(Error("Sku and Name are required."));
                if (request.Price <= 0) return Results.BadRequest(Error("Price must be greater than zero."));
                if (await db.Products.FindAsync(request.Sku) is not null)
                    return Results.Conflict(Error($"Product {request.Sku} already exists."));

                var product = new Product { Sku = request.Sku, Name = request.Name, Price = request.Price, IsActive = true };
                db.Products.Add(product);
                await db.SaveChangesAsync();

                // Contract: an absolute address. Some clients do not resolve a relative Location.
                return Results.Created($"{http.Request.Scheme}://{http.Request.Host}/api/products/{product.Sku}", product);
            });

            products.MapPut("{sku}/price", async (string sku, PriceChange request, CatalogContext db) =>
            {
                if (request.Price <= 0) return Results.BadRequest(Error("Price must be greater than zero."));
                if (await db.Products.FindAsync(sku) is not { } product) return Results.NotFound(Error($"Product {sku} was not found."));
                product.Price = request.Price;
                await db.SaveChangesAsync();
                return Results.Ok(product);
            });

            return app;
        }

        private static object Error(string message) => new { error = message };
    }

    public sealed class MoneyJsonConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetDecimal();

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteRawValue(value.ToString("0.00", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The one-off copy at cutover: reads every product through the monolith's own API and stores it here.
    /// Running it again updates what is already there, so it can be repeated until the switch.
    /// </summary>
    public class Backfill(HttpClient http, CatalogContext db)
    {
        public async Task<int> RunAsync(string monolithUrl, string apiKey)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(monolithUrl), "/api/products"));
            request.Headers.Add("X-Api-Key", apiKey);
            using var response = await http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var source = await response.Content.ReadFromJsonAsync<List<Product>>() ?? [];
            foreach (var product in source)
            {
                if (await db.Products.FindAsync(product.Sku) is { } existing)
                    (existing.Name, existing.Price, existing.IsActive) = (product.Name, product.Price, product.IsActive);
                else
                    db.Products.Add(product);
            }
            await db.SaveChangesAsync();
            return source.Count;
        }
    }
}
