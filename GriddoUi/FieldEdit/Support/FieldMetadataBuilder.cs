using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using Griddo.Editing;
using Griddo.Fields;
using Griddo.Grid;
using GriddoUi.FieldEdit.Models;

namespace GriddoUi.FieldEdit.Support;

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
        BuildRecordsFromGrid(grid, fullFieldOrder: null, previewSampleRecord: null);

    /// <summary>
    /// One record per entry in <paramref name="fullFieldOrder"/> (e.g. all registered fields). Hidden fields
    /// (not in <paramref name="grid"/>.Fields) appear with <see cref="FieldEditRecord.Visible"/> false.
    /// <see cref="FieldEditRecord.SourceFieldIndex"/> is the index into <paramref name="fullFieldOrder"/>.
    /// </summary>
    /// <param name="previewSampleRecord">When set, used for value previews instead of <see cref="Griddo.Grid.Griddo.Records"/>[0].</param>
    public static List<FieldEditRecord> BuildRecordsFromGrid(
        global::Griddo.Grid.Griddo grid,
        IReadOnlyList<IGriddoFieldView>? fullFieldOrder,
        object? previewSampleRecord = null)
    {
        object? sample = previewSampleRecord;
        if (sample is null && grid.Records.Count > 0)
        {
            sample = grid.Records[0];
        }
        var recordType = sample?.GetType();
        var nameUseCount = new Dictionary<string, int>(StringComparer.Ordinal);
        var sortMap = BuildSortMap(grid, fullFieldOrder);

        if (fullFieldOrder is null || fullFieldOrder.Count == 0)
        {
            var list = new List<FieldEditRecord>(grid.Fields.Count);
            for (var i = 0; i < grid.Fields.Count; i++)
            {
                var col = grid.Fields[i];
                list.Add(BuildOneRecord(col, i, sample, recordType, nameUseCount, visible: true, sortMap, grid, i));
            }

            return list;
        }

        var fullList = new List<FieldEditRecord>(fullFieldOrder.Count);
        for (var i = 0; i < fullFieldOrder.Count; i++)
        {
            var col = fullFieldOrder[i];
            var visible = grid.Fields.Contains(col);
            int? gridFieldIndexForWidth = null;
            if (visible)
            {
                for (var j = 0; j < grid.Fields.Count; j++)
                {
                    if (ReferenceEquals(grid.Fields[j], col))
                    {
                        gridFieldIndexForWidth = j;
                        break;
                    }
                }
            }

            fullList.Add(BuildOneRecord(col, i, sample, recordType, nameUseCount, visible, sortMap, grid,
                gridFieldIndexForWidth));
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
        Dictionary<int, GriddoSortDescriptor> sortMap,
        global::Griddo.Grid.Griddo grid,
        int? gridFieldIndexForLiveWidth)
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
            NoWrap = col is IGriddoFieldWrapView wrapView && wrapView.NoWrap,
            ForegroundColor = col is IGriddoFieldColorView colorView ? colorView.ForegroundColor : string.Empty,
            BackgroundColor = col is IGriddoFieldColorView colorView2 ? colorView2.BackgroundColor : string.Empty,
            IsNumericProperty = IsNumericField(col, sampleRaw, recordType),
            IsDateTimeProperty = IsDateTimeValueType(sampleRaw),
            IsEnumProperty = IsEnumValueType(sampleRaw),
            IsFlagsEnumProperty = IsFlagsEnumValueType(sampleRaw),
            Description = col is IGriddoFieldDescriptionView descView ? descView.Description : string.Empty,
            Visible = visible,
            FieldFill = col.FieldFill,
            Width = gridFieldIndexForLiveWidth is int gfi ? grid.GetLogicalFieldWidth(gfi) : col.Width,
            SortPriority = sortMap.TryGetValue(sourceFieldIndex, out var sd) ? sd.Priority : 0,
            SortAscending = sortMap.TryGetValue(sourceFieldIndex, out var sd2) ? sd2.Ascending : true,
            ContentAlignment = EnsureDefaultContentAlignment(
                col is IGriddoFieldAlignmentView alignmentField ? alignmentField.ContentAlignment : TextAlignment.Left,
                col,
                sampleRaw,
                recordType),
            SuppressCellEdit = gridFieldIndexForLiveWidth is int gix && grid.IsCellEditSuppressedForColumn(gix),
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
                IsEnumProperty = IsEnumType(p.PropertyType),
                IsFlagsEnumProperty = IsFlagsEnumType(p.PropertyType),
                Visible = true,
                FieldFill = 0,
                Width = 140,
                ContentAlignment = IsNumericType(p.PropertyType) ? TextAlignment.Right : TextAlignment.Left,
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

    private static bool IsEnumValueType(object? value) => value is not null && IsEnumType(value.GetType());

    private static bool IsFlagsEnumValueType(object? value) => value is not null && IsFlagsEnumType(value.GetType());

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

    private static bool IsEnumType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsEnum;
    }

    private static bool IsFlagsEnumType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsEnum && t.GetCustomAttribute<FlagsAttribute>() is not null;
    }

    /// <summary>
    /// Default cell alignment for grid settings and layout: numeric values right-align.
    /// </summary>
    public static TextAlignment ResolveDefaultContentAlignment(
        IGriddoFieldView field,
        object? sampleValue = null,
        Type? recordType = null) =>
        EnsureDefaultContentAlignment(TextAlignment.Left, field, sampleValue, recordType);

    /// <summary>
    /// Upgrades legacy left alignment on numeric fields to right while preserving explicit center/right choices.
    /// </summary>
    public static TextAlignment EnsureDefaultContentAlignment(
        TextAlignment alignment,
        IGriddoFieldView field,
        object? sampleValue = null,
        Type? recordType = null)
    {
        if (IsNumericField(field, sampleValue, recordType)
            && alignment == TextAlignment.Left)
        {
            return TextAlignment.Right;
        }

        return alignment;
    }

    /// <summary>
    /// Detects numeric grid fields from editor, sample value, bound CLR member, or numeric format string.
    /// </summary>
    public static bool IsNumericField(
        IGriddoFieldView field,
        object? sampleValue = null,
        Type? recordType = null)
    {
        if (field.Editor is GriddoNumberCellEditor)
        {
            return true;
        }

        if (IsNumericValueType(sampleValue))
        {
            return true;
        }

        if (TryGetSourceMemberPropertyType(field, recordType, out var propertyType))
        {
            if (IsDateTimeType(propertyType))
            {
                return false;
            }

            if (IsEnumType(propertyType))
            {
                return false;
            }

            if (IsNumericType(propertyType))
            {
                return true;
            }
        }

        if (field is IGriddoFieldFormatView formatView
            && LooksLikeNumericFormatString(formatView.FormatString))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetSourceMemberPropertyType(
        IGriddoFieldView field,
        Type? recordType,
        out Type propertyType)
    {
        propertyType = typeof(object);
        if (recordType is null
            || field is not IGriddoFieldSourceMember sourceMember
            || string.IsNullOrEmpty(sourceMember.SourceMemberName))
        {
            return false;
        }

        foreach (var type in EnumerateTypeHierarchy(recordType))
        {
            var property = type.GetProperty(
                sourceMember.SourceMemberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (property is null)
            {
                continue;
            }

            propertyType = property.PropertyType;
            return true;
        }

        return false;
    }

    private static IEnumerable<Type> EnumerateTypeHierarchy(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    private static bool LooksLikeNumericFormatString(string? formatString)
    {
        if (string.IsNullOrWhiteSpace(formatString))
        {
            return false;
        }

        var fmt = formatString.Trim();
        if (fmt.Length == 0)
        {
            return false;
        }

        if (IsStandardNumericFormatString(fmt))
        {
            return true;
        }

        var hasDigitPlaceholder = false;
        foreach (var ch in fmt)
        {
            switch (ch)
            {
                case '0':
                case '#':
                    hasDigitPlaceholder = true;
                    break;
                case '%':
                case 'E':
                case 'e':
                    return true;
            }
        }

        return hasDigitPlaceholder
            && fmt.IndexOfAny(['0', '#', '.', ',', '%', 'E', 'e']) >= 0;
    }

    private static bool IsStandardNumericFormatString(string format)
    {
        if (format.Length == 0)
        {
            return false;
        }

        var index = 0;
        switch (char.ToUpperInvariant(format[index]))
        {
            case 'C':
            case 'D':
            case 'E':
            case 'F':
            case 'G':
            case 'N':
            case 'P':
            case 'R':
            case 'X':
                index++;
                break;
            default:
                return false;
        }

        while (index < format.Length && char.IsDigit(format[index]))
        {
            index++;
        }

        return index == format.Length;
    }
}
