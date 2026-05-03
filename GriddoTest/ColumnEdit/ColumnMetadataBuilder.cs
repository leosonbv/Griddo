using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Griddo.Columns;

namespace GriddoTest.ColumnEdit;

/// <summary>
/// Builds <see cref="ColumnEditRow"/> entries from CLR metadata.
/// Uses <see cref="DisplayAttribute"/> for title (<see cref="DisplayAttribute.Name"/>) and description when set;
/// falls back to <see cref="DescriptionAttribute"/> for description only.
/// </summary>
/// <remarks>
/// For library-only models without data annotations, prefer a small interface (e.g. <c>IColumnDescriptor</c>)
/// or explicit metadata tables instead of reflection — annotations are convenient for POCOs and designers.
/// </remarks>
public static class ColumnMetadataBuilder
{
    /// <summary>
    /// One row per column currently in <paramref name="grid"/>; sample value from <see cref="Griddo.Rows"/>[0] when present.
    /// </summary>
    public static List<ColumnEditRow> BuildRowsFromGrid(global::Griddo.Grid.Griddo grid) =>
        BuildRowsFromGrid(grid, fullColumnOrder: null);

    /// <summary>
    /// One row per entry in <paramref name="fullColumnOrder"/> (e.g. all registered columns). Hidden columns
    /// (not in <paramref name="grid"/>.Columns) appear with <see cref="ColumnEditRow.Visible"/> false.
    /// <see cref="ColumnEditRow.SourceColumnIndex"/> is the index into <paramref name="fullColumnOrder"/>.
    /// </summary>
    public static List<ColumnEditRow> BuildRowsFromGrid(
        global::Griddo.Grid.Griddo grid,
        IReadOnlyList<IGriddoColumnView>? fullColumnOrder)
    {
        object? sample = grid.Rows.Count > 0 ? grid.Rows[0] : null;
        var rowType = sample?.GetType();
        var nameUseCount = new Dictionary<string, int>(StringComparer.Ordinal);

        if (fullColumnOrder is null || fullColumnOrder.Count == 0)
        {
            var list = new List<ColumnEditRow>(grid.Columns.Count);
            for (var i = 0; i < grid.Columns.Count; i++)
            {
                var col = grid.Columns[i];
                list.Add(BuildOneRow(col, i, sample, rowType, nameUseCount, visible: true));
            }

            return list;
        }

        var fullList = new List<ColumnEditRow>(fullColumnOrder.Count);
        for (var i = 0; i < fullColumnOrder.Count; i++)
        {
            var col = fullColumnOrder[i];
            var visible = grid.Columns.Contains(col);
            fullList.Add(BuildOneRow(col, i, sample, rowType, nameUseCount, visible));
        }

        return fullList;
    }

    private static ColumnEditRow BuildOneRow(
        IGriddoColumnView col,
        int sourceColumnIndex,
        object? sample,
        Type? rowType,
        Dictionary<string, int> nameUseCount,
        bool visible)
    {
        var baseKey = ResolveSourceMemberKey(col, sample, rowType);
        nameUseCount.TryGetValue(baseKey, out var n);
        n++;
        nameUseCount[baseKey] = n;
        var propertyName = n == 1 ? baseKey : $"{baseKey} ({n})";

        var sampleDisplay = string.Empty;
        if (sample is not null)
        {
            try
            {
                var raw = col.GetValue(sample);
                sampleDisplay = col.FormatValue(raw);
            }
            catch
            {
                sampleDisplay = string.Empty;
            }
        }

        return new ColumnEditRow
        {
            SourceColumnIndex = sourceColumnIndex,
            PropertyName = propertyName,
            Title = col.Header,
            Description = string.Empty,
            Visible = visible,
            Fill = col.Fill,
            Width = col.Width,
            SampleDisplay = sampleDisplay
        };
    }

    private static string ResolveSourceMemberKey(IGriddoColumnView col, object? sample, Type? rowType)
    {
        if (col is IGriddoColumnSourceMember sm && !string.IsNullOrEmpty(sm.SourceMemberName))
        {
            return sm.SourceMemberName;
        }

        if (sample is not null && rowType is not null)
        {
            var inferred = TryInferMemberNameFromRowType(col, sample, rowType);
            if (!string.IsNullOrEmpty(inferred))
            {
                return inferred;
            }
        }

        return col.Header;
    }

    private static string TryInferMemberNameFromRowType(IGriddoColumnView col, object sample, Type rowType)
    {
        var props = rowType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static p => p.GetIndexParameters().Length == 0 && p.CanRead)
            .ToList();

        object? colValue;
        try
        {
            colValue = col.GetValue(sample);
        }
        catch
        {
            colValue = null;
        }

        if (colValue is not null || col is not IGriddoHostedColumnView)
        {
            var valueMatches = new List<PropertyInfo>();
            foreach (var p in props)
            {
                object? pv;
                try
                {
                    pv = p.GetValue(sample);
                }
                catch
                {
                    continue;
                }

                if (ValuesEqualForBinding(pv, colValue))
                {
                    valueMatches.Add(p);
                }
            }

            if (valueMatches.Count == 1)
            {
                return valueMatches[0].Name;
            }
        }

        foreach (var p in props)
        {
            if (string.Equals(p.Name, col.Header, StringComparison.OrdinalIgnoreCase))
            {
                return p.Name;
            }

            var display = p.GetCustomAttribute<DisplayAttribute>();
            if (!string.IsNullOrWhiteSpace(display?.Name)
                && string.Equals(display.Name, col.Header, StringComparison.OrdinalIgnoreCase))
            {
                return p.Name;
            }
        }

        return string.Empty;
    }

    private static bool ValuesEqualForBinding(object? a, object? b)
    {
        if (a is null && b is null)
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        if (a is double da && b is double db)
        {
            return da.Equals(db);
        }

        if (a is float fa && b is float fb)
        {
            return fa.Equals(fb);
        }

        if (a is IFormattable && b is IFormattable && a.GetType() == b.GetType())
        {
            return string.Equals(
                a.ToString(),
                b.ToString(),
                StringComparison.Ordinal);
        }

        return Equals(a, b);
    }

    public static List<ColumnEditRow> BuildRows(Type reflectedType, object? sampleRow, IComparer<PropertyInfo>? propertyOrder = null)
    {
        var order = propertyOrder ?? Comparer<PropertyInfo>.Create(ComparePropertiesForColumnChooser);
        var props = reflectedType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead)
            .OrderBy(p => p, order)
            .ToList();

        var rows = new List<ColumnEditRow>(props.Count);
        foreach (var p in props)
        {
            var display = p.GetCustomAttribute<DisplayAttribute>();
            var descAttr = p.GetCustomAttribute<DescriptionAttribute>();
            var title = !string.IsNullOrWhiteSpace(display?.Name)
                ? display!.Name!
                : p.Name;
            var description = !string.IsNullOrWhiteSpace(display?.GetDescription())
                ? display!.GetDescription()!
                : descAttr?.Description ?? string.Empty;

            string sample = string.Empty;
            if (sampleRow is not null)
            {
                try
                {
                    var raw = p.GetValue(sampleRow);
                    sample = FormatSample(raw);
                }
                catch
                {
                    sample = string.Empty;
                }
            }

            rows.Add(new ColumnEditRow
            {
                PropertyName = p.Name,
                Title = title,
                Description = description,
                Visible = true,
                Fill = false,
                Width = 140,
                SampleDisplay = sample
            });
        }

        return rows;
    }

    private static int ComparePropertiesForColumnChooser(PropertyInfo a, PropertyInfo b)
    {
        var oa = a.GetCustomAttribute<DisplayAttribute>()?.GetOrder();
        var ob = b.GetCustomAttribute<DisplayAttribute>()?.GetOrder();
        if (oa.HasValue && ob.HasValue && oa.Value != ob.Value)
        {
            return oa.Value.CompareTo(ob.Value);
        }

        if (oa.HasValue != ob.HasValue)
        {
            return oa.HasValue ? -1 : 1;
        }

        return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
    }

    private static string FormatSample(object? value) =>
        value switch
        {
            null => string.Empty,
            string s => s,
            IFormattable f => f.ToString(null, CultureInfo.CurrentCulture),
            _ => value.ToString() ?? string.Empty
        };
}
