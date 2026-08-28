using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CryptoScanner.UI;

public sealed class EdgeColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
            return d >= 0 ? Brushes.DarkGreen : Brushes.DarkRed;

        if (value is decimal m)
            return m >= 0 ? Brushes.DarkGreen : Brushes.DarkRed;

        if (value is float f)
            return f >= 0 ? Brushes.DarkGreen : Brushes.DarkRed;

        if (value is int i)
            return i >= 0 ? Brushes.DarkGreen : Brushes.DarkRed;

        if (value is long l)
            return l >= 0 ? Brushes.DarkGreen : Brushes.DarkRed;

        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
