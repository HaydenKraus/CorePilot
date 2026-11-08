using System.Globalization;
using System.Windows;
using System.Windows.Data;
using CorePilot.App.Localization;

namespace CorePilot.App.Converters;

/// <summary>True → Collapsed, false/null → Visible. Used for empty-state overlays.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool flag && flag ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility visibility && visibility != Visibility.Visible;
}

/// <summary>Maps the process list group key (IsApp) to a localized section caption.</summary>
public sealed class ProcessGroupNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true
            ? LocalizationService.Instance["process.group.apps"]
            : LocalizationService.Instance["process.group.background"];

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
