using System;
using System.Globalization;
using System.Windows.Data;

namespace CryptoScanner.UI;

public sealed class EdgeColorConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        return value switch
        {
            double d => d >= 0
                ? System.Windows.Media.Brushes.DarkGreen
                : System.Windows.Media.Brushes.DarkRed,

            decimal d => d >= 0
                ? System.Windows.Media.Brushes.DarkGreen
                : System.Windows.Media.Brushes.DarkRed,

            float d => d >= 0
                ? System.Windows.Media.Brushes.DarkGreen
                : System.Windows.Media.Brushes.DarkRed,

            int d => d >= 0
                ? System.Windows.Media.Brushes.DarkGreen
                : System.Windows.Media.Brushes.DarkRed,

            long d => d >= 0
                ? System.Windows.Media.Brushes.DarkGreen
                : System.Windows.Media.Brushes.DarkRed,

            _ => System.Windows.Media.Brushes.Gray
        };
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
        => throw new NotSupportedException();
}