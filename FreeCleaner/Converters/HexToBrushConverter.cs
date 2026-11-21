using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FreeCleaner.Converters;

/// <summary>
/// Converts a hex color string (for example "#2563EB") into a cached, frozen SolidColorBrush.
/// </summary>
public sealed class HexToBrushConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, SolidColorBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
            return Brushes.SteelBlue;

        return BrushCache.GetOrAdd(hex.Trim(), static key =>
        {
            try
            {
                if (ColorConverter.ConvertFromString(key) is Color parsed)
                {
                    var brush = new SolidColorBrush(parsed);
                    if (brush.CanFreeze)
                        brush.Freeze();
                    return brush;
                }
            }
            catch
            {
                // Fall back safely when invalid hex color strings are supplied.
            }

            return Brushes.SteelBlue;
        });
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
