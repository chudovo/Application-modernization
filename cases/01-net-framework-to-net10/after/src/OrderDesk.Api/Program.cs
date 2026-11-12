using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderDesk.Api.Infrastructure;
using OrderDesk.Data;
using OrderDesk.Domain;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PricingOptions>(builder.Configuration.GetSection("Pricing"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<PricingOptions>>().Value);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PricingService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddDbContext<OrderDeskContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("OrderDesk")));

builder.Services.AddControllers()
    // Web API 2 wrote property names exactly as declared in C#. ASP.NET Core camel-cases them by default.
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = null)
    .ConfigureApiBehaviorOptions(o =>
    {
        // [ApiController] answers invalid input by itself with a ProblemDetails body. The legacy API let the
        // action run and returned { "Message": ... }, and clients parse that shape.
        o.SuppressModelStateInvalidFilter = true;
        // Likewise, NotFound() would get a ProblemDetails body. The legacy API returned 404 with no body.
        o.SuppressMapClientErrors = true;
    });

// Responses written outside MVC (the API key middleware) use a separate set of JSON options.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = null);

var app = builder.Build();

app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();

app.Run();

public partial class Program;
