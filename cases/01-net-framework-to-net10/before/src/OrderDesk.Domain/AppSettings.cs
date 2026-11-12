using System.Configuration;
using System.Globalization;

namespace OrderDesk.Domain
{
    /// <summary>
    /// Static access to App.config / Web.config. Every class that needs a setting calls this directly,
    /// which is why none of them can be tested without a config file.
    /// </summary>
    public static class AppSettings
    {
        public static decimal VatRate
        {
            get { return decimal.Parse(ConfigurationManager.AppSettings["VatRate"] ?? "0.19", CultureInfo.InvariantCulture); }
        }

        public static string ApiKey
        {
            get { return ConfigurationManager.AppSettings["ApiKey"]; }
        }
    }
}
