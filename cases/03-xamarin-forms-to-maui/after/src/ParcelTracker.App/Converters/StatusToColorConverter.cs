using System.Globalization;

namespace ParcelTracker.App.Converters;

public class StatusToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value as string switch
    {
        "Delivered" => Color.FromArgb("#2E7D32"),
        "InTransit" => Color.FromArgb("#1F6FD6"),
        "Returned" => Color.FromArgb("#C62828"),
        _ => Color.FromArgb("#616161")
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
