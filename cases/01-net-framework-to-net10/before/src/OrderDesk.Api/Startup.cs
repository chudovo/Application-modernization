using System.Web.Http;
using OrderDesk.Api.Infrastructure;
using Owin;

namespace OrderDesk.Api
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            var config = new HttpConfiguration();
            config.MapHttpAttributeRoutes();
            config.MessageHandlers.Add(new ApiKeyHandler());

            // JSON only. The formatter is Json.NET with its defaults: property names as written in C#,
            // enums as numbers, case-insensitive matching and string-to-number coercion on input.
            config.Formatters.Remove(config.Formatters.XmlFormatter);

            app.UseWebApi(config);
        }
    }
}
