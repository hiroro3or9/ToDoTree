using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ToDoTree.App.ViewModels;
using ToDoTree.Core.Models;

namespace ToDoTree.App.Converters;

/// <summary>列挙値を日本語ラベルに変換する（ComboBox 用）。</summary>
public sealed class EnumLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        NodeStatus status => Labels.Of(status),
        NodeKind kind => Labels.Of(kind),
        Readiness readiness => Labels.Of(readiness),
        _ => value?.ToString() ?? string.Empty,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>null なら Collapsed。</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>true なら Visible（パラメータに "invert" を渡すと反転）。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
