using System;
using System.Globalization;
using Xamarin.Forms;

namespace ParcelTracker.Converters
{
    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value as string)
            {
                case "Delivered": return Color.FromHex("#2E7D32");
                case "InTransit": return Color.FromHex("#1F6FD6");
                case "Returned": return Color.FromHex("#C62828");
                default: return Color.FromHex("#616161");
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
