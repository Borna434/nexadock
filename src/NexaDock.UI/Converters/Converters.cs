using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NexaDock.UI.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is true;
        return flag ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility v && (v == Visibility.Visible) ^ Invert;
}

/// <summary>Formats a 0..1 value as a whole percentage.</summary>
public sealed class PercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? $"{d * 100:0}%" : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
