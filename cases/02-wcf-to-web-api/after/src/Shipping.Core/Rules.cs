namespace Shipping.Core;

public static class Rules
{
    public const string CountryCodeError = "CountryCode must be a two-letter ISO code.";
    public const string WeightError = "WeightKg must be greater than 0 and at most 30.";
    public const string RecipientError = "RecipientName is required.";

    public static bool IsCountryCode(string? value) => value is { Length: 2 } && value.All(char.IsLetter);

    public static bool IsWeight(decimal value) => value is > 0m and <= 30m;
}

public class RateCalculator
{
    private static readonly HashSet<string> EuCountries = new(StringComparer.OrdinalIgnoreCase)
    {
        "AT", "BE", "CZ", "DK", "ES", "FI", "FR", "IE", "IT", "LU", "NL", "PL", "PT", "SE"
    };

    public decimal Quote(decimal weightKg, string? countryCode, ServiceLevel service)
    {
        var errors = new List<string>();
        if (!Rules.IsCountryCode(countryCode)) errors.Add(Rules.CountryCodeError);
        if (!Rules.IsWeight(weightKg)) errors.Add(Rules.WeightError);
        if (errors.Count > 0) throw new ValidationFailedException(errors);
        return Rate(weightKg, countryCode!, service);
    }

    internal static decimal Rate(decimal weightKg, string countryCode, ServiceLevel service)
    {
        var basePrice = string.Equals(countryCode, "DE", StringComparison.OrdinalIgnoreCase) ? 4.90m
            : EuCountries.Contains(countryCode) ? 9.90m
            : 19.90m;
        var extraKilograms = Math.Max(0m, Math.Ceiling(weightKg - 1m));
        var price = basePrice + extraKilograms * 1.20m;
        if (service == ServiceLevel.Express) price *= 1.5m;
        return Math.Round(price, 2, MidpointRounding.AwayFromZero);
    }
}
