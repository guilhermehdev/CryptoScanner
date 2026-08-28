using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CryptoScanner.UI;

public sealed class EdgeColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            double d => d >= 0 ?  Brushes.DarkGreen : Brushes.DarkRed,
            decimal d => d >= 0 ? Brushes.DarkGreen : Brushes.DarkRed,
            float d => d >= 0 ? Brushes.DarkGreen : Brushes.DarkRed,
            int d => d >= 0 ? Brushes.DarkGreen : Brushes.DarkRed,
            long d => d >= 0 ? Brushes.DarkGreen : Brushes.DarkRed,
            _ => Brushes.Gray
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
