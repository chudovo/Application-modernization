using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Seminars.Web.Infrastructure;

/// <summary>
/// Reads decimals from forms the way ASP.NET MVC 5 did: a decimal separator of the current culture, no thousands
/// separator. ASP.NET Core also accepts thousands separators, and on a German system that turns "750.50" into
/// 75050 without any error. The old application answered that input with a validation message. So does this.
/// </summary>
public sealed class LegacyDecimalModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext context)
    {
        var value = context.ValueProvider.GetValue(context.ModelName);
        if (value == ValueProviderResult.None) return Task.CompletedTask;
        context.ModelState.SetModelValue(context.ModelName, value);

        var text = value.FirstValue;
        if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;

        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number))
            context.Result = ModelBindingResult.Success(number);
        else
            context.ModelState.TryAddModelError(context.ModelName, $"The value '{text}' is not valid for {context.ModelMetadata.GetDisplayName()}.");
        return Task.CompletedTask;
    }
}

public sealed class LegacyDecimalModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context) =>
        context.Metadata.UnderlyingOrModelType == typeof(decimal) ? new LegacyDecimalModelBinder() : null;
}
