using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using Seminars.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(mvc => mvc.ModelBinderProviders.Insert(0, new LegacyDecimalModelBinderProvider()))
    .AddJsonOptions(json =>
    {
        // Json() in ASP.NET MVC 5 wrote property names as declared and dates as "/Date(milliseconds)/".
        // A partner site parses exactly that.
        json.JsonSerializerOptions.PropertyNamingPolicy = null;
        json.JsonSerializerOptions.Converters.Add(new LegacyDateConverter());
    });

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();

var app = builder.Build();

// Web.config had <globalization culture="de-DE" uiCulture="de-DE" />: one culture for everyone, whatever the browser
// asks for. ASP.NET Core has no such setting. Without this block the server's own culture is used, which in a Linux
// container is the invariant one.
var german = new CultureInfo("de-DE");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(german),
    SupportedCultures = [german],
    SupportedUICultures = [german],
    RequestCultureProviders = [],
});

app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultControllerRoute();

app.Run();

public sealed class LegacyDateConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => throw new NotSupportedException();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue($"/Date({new DateTimeOffset(value.ToUniversalTime()).ToUnixTimeMilliseconds()})/");
}
