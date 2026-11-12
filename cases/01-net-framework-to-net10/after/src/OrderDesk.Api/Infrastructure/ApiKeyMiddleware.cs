namespace OrderDesk.Api.Infrastructure;

/// <summary>Replaces the Web API DelegatingHandler: checks X-Api-Key for every route except the health check.</summary>
public class ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/health"))
        {
            var supplied = context.Request.Headers["X-Api-Key"].FirstOrDefault();
            if (string.IsNullOrEmpty(supplied) || supplied != configuration["ApiKey"])
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new ErrorBody("API key is missing or invalid."));
                return;
            }
        }

        await next(context);
    }
}
