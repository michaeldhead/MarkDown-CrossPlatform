using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GhsMarkdown.Cross.Views;

/// <summary>
/// Converts a heading level (1-6) and IsActive flag to the appropriate color brush.
/// Used in TopologyView node template for bullet and text coloring.
/// </summary>
public class HeadingLevelToBrushConverter : IMultiValueConverter
{
    private static readonly IBrush[] LevelBrushes =
    {
        Brushes.Transparent,
        new SolidColorBrush(Color.FromRgb(0x4A, 0x9E, 0xFF)), // H1
        new SolidColorBrush(Color.FromRgb(0x5A, 0xB8, 0x65)), // H2
        new SolidColorBrush(Color.FromRgb(0xB8, 0x95, 0x4A)), // H3
        new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), // H4
        new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)), // H5
        new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), // H6
    };

    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x9E, 0xFF));

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2) return Brushes.Transparent;

        var level = values[0] is int l ? l : 1;
        var isActive = values[1] is bool a && a;

        if (isActive) return AccentBrush;
        if (level >= 1 && level <= 6) return LevelBrushes[level];
        return LevelBrushes[1];
    }
}

/// <summary>
/// Simple converter: heading level (1-6) → brush. No active state consideration.
/// </summary>
public class HeadingLevelBrushConverter : IValueConverter
{
    private static readonly IBrush[] LevelBrushes =
    {
        Brushes.Transparent,
        new SolidColorBrush(Color.FromRgb(0x4A, 0x9E, 0xFF)), // H1
        new SolidColorBrush(Color.FromRgb(0x5A, 0xB8, 0x65)), // H2
        new SolidColorBrush(Color.FromRgb(0xB8, 0x95, 0x4A)), // H3
        new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), // H4
        new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)), // H5
        new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), // H6
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = value is int l ? l : 1;
        if (level >= 1 && level <= 6) return LevelBrushes[level];
        return LevelBrushes[1];
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts WidthFraction (0.0–1.0) to pixel width for balance bars.
/// Panel is 220px with 8px padding each side → ~196px available.
/// </summary>
public class FractionToWidthConverter : IValueConverter
{
    private const double MaxWidth = 196.0;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double fraction)
            return Math.Max(4, fraction * MaxWidth); // Min 4px so tiny sections are visible
        return 4.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
