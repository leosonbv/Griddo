using System.Reflection;
using Griddo.Editing;

namespace Griddo.Columns;

/// <summary>Factory for columns bound to a named source object inside a row dictionary.</summary>
public static class GriddoNamedSourceColumns
{
    public static GriddoColumnView Create(
        string sourceObjectName,
        string sourceMemberName,
        string header,
        double width,
        IGriddoCellEditor? editor = null,
        System.Windows.TextAlignment? contentAlignment = null,
        bool fill = false)
    {
        var getter = CreateGetter(sourceObjectName, sourceMemberName);
        var setter = CreateSetter(sourceObjectName, sourceMemberName);
        return new GriddoColumnView(
            header: header,
            width: width,
            valueGetter: getter,
            valueSetter: setter,
            editor: editor,
            contentAlignment: contentAlignment,
            fill: fill,
            sourceMemberName: sourceMemberName,
            sourceObjectName: sourceObjectName);
    }

    private static Func<object, object?> CreateGetter(string sourceObjectName, string sourceMemberName)
    {
        PropertyInfo? property = null;
        return rowSource =>
        {
            if (!TryGetNamedSource(rowSource, sourceObjectName, out var source) || source is null)
            {
                return null;
            }

            property ??= source.GetType().GetProperty(sourceMemberName, BindingFlags.Instance | BindingFlags.Public);
            return property?.GetValue(source);
        };
    }

    private static Func<object, object?, bool> CreateSetter(string sourceObjectName, string sourceMemberName)
    {
        PropertyInfo? property = null;
        return (rowSource, value) =>
        {
            if (!TryGetNamedSource(rowSource, sourceObjectName, out var source) || source is null)
            {
                return false;
            }

            property ??= source.GetType().GetProperty(sourceMemberName, BindingFlags.Instance | BindingFlags.Public);
            if (property is null || !property.CanWrite)
            {
                return false;
            }

            try
            {
                var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                object? converted = value;
                if (value is not null && !targetType.IsInstanceOfType(value))
                {
                    converted = Convert.ChangeType(value, targetType);
                }

                property.SetValue(source, converted);
                return true;
            }
            catch
            {
                return false;
            }
        };
    }

    public static bool TryGetNamedSource(object rowSource, string sourceObjectName, out object? source)
    {
        source = null;
        if (rowSource is IReadOnlyDictionary<string, object?> readOnly && readOnly.TryGetValue(sourceObjectName, out var ro))
        {
            source = ro;
            return true;
        }

        if (rowSource is IDictionary<string, object?> map && map.TryGetValue(sourceObjectName, out var m))
        {
            source = m;
            return true;
        }

        if (rowSource is IDictionary<string, object> nonNullable && nonNullable.TryGetValue(sourceObjectName, out var nn))
        {
            source = nn;
            return true;
        }

        return false;
    }
}
