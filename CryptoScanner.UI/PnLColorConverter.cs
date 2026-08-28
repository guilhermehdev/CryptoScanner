using System;
using System.Globalization;
using System.Windows.Data;

namespace CryptoScanner.UI;

public sealed class PnLColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal pnl)
        {
            if (pnl > 0m)
                return System.Windows.Media.Brushes.DarkGreen;

            if (pnl < 0m)
                return System.Windows.Media.Brushes.DarkRed;
        }

        return System.Windows.Media.Brushes.Black;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
