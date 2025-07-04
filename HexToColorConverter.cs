using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace AntennaAV
{
    public class HexToColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string hex && Color.TryParse(hex, out var color))
                return color;
            return Colors.Blue;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is Color color)
                return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            return "#0000FF";
        }
    }
} 