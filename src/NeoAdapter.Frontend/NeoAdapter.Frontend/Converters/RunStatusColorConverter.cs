using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace NeoAdapter.Frontend.Converters;

public sealed class RunStatusColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string status)
        {
            return status.ToUpperInvariant() switch
            {
                "SUCCEEDED" => Brushes.SpringGreen,
                "FAILED" => Brushes.Crimson,
                "RUNNING" => Brushes.DeepSkyBlue,
                "QUEUED" => Brushes.Orange,
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
