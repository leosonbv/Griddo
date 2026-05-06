using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Griddo.Fields;
using Griddo.Grid;

namespace GriddoUi.FieldEdit;

/// <summary>
/// Builds <see cref="FieldEditRecord"/> entries from CLR metadata.
/// Uses <see cref="DisplayAttribute"/> for title (<see cref="DisplayAttribute.Name"/>) and description when set;
/// falls back to <see cref="DescriptionAttribute"/> for description only.
/// </summary>
/// <remarks>
/// For library-only models without data annotations, prefer a small interface (e.g. <c>IFieldDescriptor</c>)
/// or explicit metadata tables instead of reflection - annotations are convenient for POCOs and designers.
/// </remarks>
public static class FieldMetadataBuilder
{
    /// <summary>
    /// One record per field currently in <paramref name="grid"/>; sample value from <see cref="Griddo.Records"/>[0] when present.
    /// </summary>
    public static List<FieldEditRecord> BuildRecordsFromGrid(global::Griddo.Grid.Griddo grid) =>
        BuildRecordsFromGrid(grid, fullFieldOrder: null);

    /// <summary>
    /// One record per entry in <paramref name="fullFieldOrder"/> (e.g. all registered fields). Hidden fields
    /// (not in <paramref name="grid"/>.Fields) appear with <see cref="FieldEditRecord.Visible"/> false.
    /// <see cref="FieldEditRecord.SourceFieldIndex"/> is the index into <paramref name="fullFieldOrder"/>.
    /// </summary>
    public static List<FieldEditRecord> BuildRecordsFromGrid(
        global::Griddo.Grid.Griddo grid,
        IReadOnlyList<IGriddoFieldView>? fullFieldOrder)
    {
        object? sample = grid.Records.Count > 0 ? grid.Records[0] : null;
        var recordType = sample?.GetType();
        var nameUseCount = new Dictionary<string, int>(StringComparer.Ordinal);
        var sortMap = BuildSortMap(grid, fullFieldOrder);

        if (fullFieldOrder is null || fullFieldOrder.Count == 0)
        {
            var list = new List<FieldEditRecord>(grid.Fields.Count);
            for (var i = 0; i < grid.Fields.Count; i++)
            {
                var col = grid.Fields[i];
                list.Add(BuildOneRecord(col, i, sample, recordType, nameUseCount, visible: true, sortMap));
            }

            return list;
        }

        var fullList = new List<FieldEditRecord>(fullFieldOrder.Count);
        for (var i = 0; i < fullFieldOrder.Count; i++)
        {
            var col = fullFieldOrder[i];
            var visible = grid.Fields.Contains(col);
            fullList.Add(BuildOneRecord(col, i, sample, recordType, nameUseCount, visible, sortMap));
        }

        return fullList;
    }

    private static FieldEditRecord BuildOneRecord(
        IGriddoFieldView col,
        int sourceFieldIndex,
        object? sample,
        Type? recordType,
        Dictionary<string, int> nameUseCount,
        bool visible,
        Dictionary<int, GriddoSortDescriptor> sortMap)
    {
        var baseKey = ResolveSourceMemberKey(col, sample, recordType);
        nameUseCount.TryGetValue(baseKey, out var n);
        n++;
        nameUseCount[baseKey] = n;
        var propertyName = n == 1 ? baseKey : $"{baseKey} ({n})";

        var sampleDisplay = string.Empty;
        object? sampleRaw = null;
        if (sample is not null)
        {
            try
            {
                sampleRaw = col.GetValue(sample);
                sampleDisplay = col.FormatValue(sampleRaw);
            }
            catch
            {
                sampleDisplay = string.Empty;
            }
        }

        return new FieldEditRecord
        {
            SourceFieldIndex = sourceFieldIndex,
            PropertyName = propertyName,
            SourceObjectName = col is IGriddoFieldSourceObject sourceObject ? sourceObject.SourceObjectName : string.Empty,
            Title = col.Header,
            AbbreviatedTitle = col is IGriddoFieldTitleView titleView ? titleView.AbbreviatedHeader : string.Empty,
            FormatString = col is IGriddoFieldFormatView formatView ? formatView.FormatString : string.Empty,
            FontFamilyName = col is IGriddoFieldFontView fontView ? fontView.FontFamilyName : string.Empty,
            FontSize = col is IGriddoFieldFontView fontView2 ? fontView2.FontSize : 0,
            FontStyleName = col is IGriddoFieldFontView fontView3 ? fontView3.FontStyleName : string.Empty,
            ForegroundColor = col is IGriddoFieldColorView colorView ? colorView.ForegroundColor : string.Empty,
            BackgroundColor = col is IGriddoFieldColorView colorView2 ? colorView2.BackgroundColor : string.Empty,
            IsNumericProperty = IsNumericValueType(sampleRaw),
            IsDateTimeProperty = IsDateTimeValueType(sampleRaw),
            Description = col is IGriddoFieldDescriptionView descView ? descView.Description : string.Empty,
            Visible = visible,
            Fill = col.Fill,
            Width = col.Width,
            SortPriority = sortMap.TryGetValue(sourceFieldIndex, out var sd) ? sd.Priority : 0,
            SortAscending = sortMap.TryGetValue(sourceFieldIndex, out var sd2) ? sd2.Ascending : true,
            SampleDisplay = sampleDisplay,
            SampleValue = sampleRaw,
            SampleRecordSource = sample,
            SourceFieldView = col
        };
    }

    private static Dictionary<int, GriddoSortDescriptor> BuildSortMap(
        global::Griddo.Grid.Griddo grid,
        IReadOnlyList<IGriddoFieldView>? fullFieldOrder)
    {
        if (fullFieldOrder is null || fullFieldOrder.Count == 0)
        {
            return grid.SortDescriptors.ToDictionary(d => d.FieldIndex);
        }

        var map = new Dictionary<int, GriddoSortDescriptor>();
        foreach (var d in grid.SortDescriptors)
        {
            if (d.FieldIndex < 0 || d.FieldIndex >= grid.Fields.Count)
            {
                continue;
            }

            var col = grid.Fields[d.FieldIndex];
            var sourceIndex = -1;
            for (var i = 0; i < fullFieldOrder.Count; i++)
            {
                if (ReferenceEquals(fullFieldOrder[i], col))
                {
                    sourceIndex = i;
                    break;
                }
            }

            if (sourceIndex >= 0)
            {
                map[sourceIndex] = new GriddoSortDescriptor(sourceIndex, d.Ascending, d.Priority);
            }
        }

        return map;
    }

    private static string ResolveSourceMemberKey(IGriddoFieldView col, object? sample, Type? recordType)
    {
        if (col is IGriddoFieldSourceMember sm && !string.IsNullOrEmpty(sm.SourceMemberName))
        {
            return sm.SourceMemberName;
        }

        if (sample is not null && recordType is not null)
        {
            var inferred = TryInferMemberNameFromRecordType(col, sample, recordType);
            if (!string.IsNullOrEmpty(inferred))
            {
                return inferred;
            }
        }

        return col.Header;
    }

    private static string TryInferMemberNameFromRecordType(IGriddoFieldView col, object sample, Type recordType)
    {
        var props = recordType
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

        if (colValue is not null || col is not IGriddoHostedFieldView)
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

    public static List<FieldEditRecord> BuildRecords(Type reflectedType, object? sampleRecord, IComparer<PropertyInfo>? propertyOrder = null)
    {
        var order = propertyOrder ?? Comparer<PropertyInfo>.Create(ComparePropertiesForFieldChooser);
        var props = reflectedType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead)
            .OrderBy(p => p, order)
            .ToList();

        var records = new List<FieldEditRecord>(props.Count);
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
            object? sampleValue = null;
            if (sampleRecord is not null)
            {
                try
                {
                    sampleValue = p.GetValue(sampleRecord);
                    sample = FormatSample(sampleValue);
                }
                catch
                {
                    sample = string.Empty;
                }
            }

            records.Add(new FieldEditRecord
            {
                PropertyName = p.Name,
                Title = title,
                Description = description,
                IsNumericProperty = IsNumericType(p.PropertyType),
                IsDateTimeProperty = IsDateTimeType(p.PropertyType),
                Visible = true,
                Fill = false,
                Width = 140,
                SampleDisplay = sample,
                SampleValue = sampleValue,
                SampleRecordSource = sampleRecord
            });
        }

        return records;
    }

    private static int ComparePropertiesForFieldChooser(PropertyInfo a, PropertyInfo b)
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

    private static bool IsNumericValueType(object? value) => value is not null && IsNumericType(value.GetType());

    private static bool IsDateTimeValueType(object? value) => value is not null && IsDateTimeType(value.GetType());

    private static bool IsNumericType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t == typeof(byte) || t == typeof(sbyte)
            || t == typeof(short) || t == typeof(ushort)
            || t == typeof(int) || t == typeof(uint)
            || t == typeof(long) || t == typeof(ulong)
            || t == typeof(float) || t == typeof(double)
            || t == typeof(decimal);
    }

    private static bool IsDateTimeType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t == typeof(DateTime)
            || t == typeof(DateTimeOffset)
            || t == typeof(DateOnly)
            || t == typeof(TimeOnly)
            || t == typeof(TimeSpan);
    }
}
