using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace NeoAdapter.Frontend.Converters;

public sealed class LogLevelColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string level)
        {
            return level.ToUpperInvariant() switch
            {
                "ERROR" or "ERR" => Brushes.Tomato,
                "WARN" or "WARNING" => Brushes.Gold,
                "INFO" => Brushes.LightSkyBlue,
                _ => Brushes.LightGray
            };
        }
        return Brushes.LightGray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
