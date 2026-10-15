using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Silkfly.App.Converters;

public sealed class JoinConverter : IValueConverter
{
    public static readonly JoinConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is IEnumerable items and not string ? string.Join(", ", items.Cast<object>()) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
