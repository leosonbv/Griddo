using System.Globalization;
using System.Reflection;
using System.Windows;
using Griddo.Editing;
using Griddo.Fields.Attributes;

namespace Griddo.Fields;

public sealed class GriddoEnumFieldView<TEnum> : IGriddoFieldView, IGriddoFieldSourceMember, IGriddoFieldSourceObject, IGriddoFieldTitleView, IGriddoFieldDescriptionView, IGriddoFieldFormatView, IGriddoFieldFontView, IGriddoFieldColorView, IGriddoDynamicFieldColorView, IGriddoFieldWrapView
    where TEnum : struct, Enum
{
    private readonly Func<object, TEnum> _valueGetter;
    private readonly Func<object, TEnum, bool> _valueSetter;
    private readonly GriddoOptionsCellEditor _editor;
    private readonly Dictionary<string, TEnum> _nameToValue;

    public GriddoEnumFieldView(
        string header,
        double width,
        Func<object, TEnum> valueGetter,
        Func<object, TEnum, bool> valueSetter,
        bool fill = false,
        string? sourceMemberName = null,
        string? sourceObjectName = null)
    {
        Header = header;
        Width = width;
        Fill = fill;
        SourceMemberName = sourceMemberName ?? string.Empty;
        SourceObjectName = sourceObjectName ?? string.Empty;
        _valueGetter = valueGetter;
        _valueSetter = valueSetter;
        _nameToValue = Enum.GetNames<TEnum>().ToDictionary(static n => n, static n => Enum.Parse<TEnum>(n), StringComparer.OrdinalIgnoreCase);
        _editor = new GriddoOptionsCellEditor(_nameToValue.Keys, allowMultiple: false, allowEmpty: false);
    }

    public string Header { get; set; }
    public string AbbreviatedHeader { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string FormatString { get; set; } = string.Empty;
    public string FontFamilyName { get; set; } = string.Empty;
    public double FontSize { get; set; }
    public string FontStyleName { get; set; } = string.Empty;
    public string ForegroundColor { get; set; } = string.Empty;
    public string BackgroundColor { get; set; } = string.Empty;
    public bool NoWrap { get; set; } = true;
    public string SourceMemberName { get; }
    public string SourceObjectName { get; }
    public double Width { get; }
    public bool Fill { get; set; }
    public bool IsHtml => false;
    public TextAlignment ContentAlignment => TextAlignment.Left;
    public IGriddoCellEditor Editor => _editor;

    public object? GetValue(object recordSource) => _valueGetter(recordSource);

    public bool TrySetValue(object recordSource, object? value)
    {
        if (value is TEnum e)
        {
            return _valueSetter(recordSource, e);
        }

        var text = value?.ToString()?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return false;
        }

        if (_nameToValue.TryGetValue(text, out var parsed))
        {
            return _valueSetter(recordSource, parsed);
        }

        return false;
    }

    public string FormatValue(object? value) =>
        value is TEnum e ? e.ToString() : string.Empty;

    public string GetForegroundColor(object recordSource) => ForegroundColor;

    public string GetBackgroundColor(object recordSource)
    {
        if (!string.IsNullOrWhiteSpace(BackgroundColor))
        {
            return BackgroundColor;
        }

        var value = _valueGetter(recordSource);
        return EnumColorLookup.GetColor(value);
    }
}

public sealed class GriddoFlagsFieldView<TEnum> : IGriddoFieldView, IGriddoFieldSourceMember, IGriddoFieldSourceObject, IGriddoFieldTitleView, IGriddoFieldDescriptionView, IGriddoFieldFormatView, IGriddoFieldFontView, IGriddoFieldColorView, IGriddoDynamicFieldColorView, IGriddoFieldWrapView
    where TEnum : struct, Enum
{
    private readonly Func<object, TEnum> _valueGetter;
    private readonly Func<object, TEnum, bool> _valueSetter;
    private readonly GriddoOptionsCellEditor _editor;
    private readonly Dictionary<string, TEnum> _nameToValue;

    public GriddoFlagsFieldView(
        string header,
        double width,
        Func<object, TEnum> valueGetter,
        Func<object, TEnum, bool> valueSetter,
        bool fill = false,
        string? sourceMemberName = null,
        string? sourceObjectName = null)
    {
        if (typeof(TEnum).GetCustomAttribute<FlagsAttribute>() is null)
        {
            throw new ArgumentException($"{typeof(TEnum).Name} is not marked with [Flags].");
        }

        Header = header;
        Width = width;
        Fill = fill;
        SourceMemberName = sourceMemberName ?? string.Empty;
        SourceObjectName = sourceObjectName ?? string.Empty;
        _valueGetter = valueGetter;
        _valueSetter = valueSetter;
        _nameToValue = Enum.GetNames<TEnum>().ToDictionary(static n => n, static n => Enum.Parse<TEnum>(n), StringComparer.OrdinalIgnoreCase);
        _editor = new GriddoOptionsCellEditor(_nameToValue.Keys, allowMultiple: true, allowEmpty: true);
    }

    public string Header { get; set; }
    public string AbbreviatedHeader { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string FormatString { get; set; } = string.Empty;
    public string FontFamilyName { get; set; } = string.Empty;
    public double FontSize { get; set; }
    public string FontStyleName { get; set; } = string.Empty;
    public string ForegroundColor { get; set; } = string.Empty;
    public string BackgroundColor { get; set; } = string.Empty;
    public bool NoWrap { get; set; } = true;
    public string SourceMemberName { get; }
    public string SourceObjectName { get; }
    public double Width { get; }
    public bool Fill { get; set; }
    public bool IsHtml => false;
    public TextAlignment ContentAlignment => TextAlignment.Left;
    public IGriddoCellEditor Editor => _editor;

    public object? GetValue(object recordSource) => _valueGetter(recordSource);

    public bool TrySetValue(object recordSource, object? value)
    {
        if (value is TEnum e)
        {
            return _valueSetter(recordSource, e);
        }

        var text = value?.ToString()?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return _valueSetter(recordSource, default);
        }

        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        ulong combined = 0;
        foreach (var part in parts)
        {
            if (!_nameToValue.TryGetValue(part, out var flag))
            {
                return false;
            }

            combined |= Convert.ToUInt64(flag, CultureInfo.InvariantCulture);
        }

        var typed = (TEnum)Enum.ToObject(typeof(TEnum), combined);
        return _valueSetter(recordSource, typed);
    }

    public string FormatValue(object? value)
    {
        if (value is not TEnum flags)
        {
            return string.Empty;
        }

        return flags.ToString();
    }

    public string GetForegroundColor(object recordSource) => ForegroundColor;

    public string GetBackgroundColor(object recordSource)
    {
        if (!string.IsNullOrWhiteSpace(BackgroundColor))
        {
            return BackgroundColor;
        }

        var flags = _valueGetter(recordSource);
        var active = Enum.GetValues<TEnum>()
            .Where(flag => Convert.ToUInt64(flag, CultureInfo.InvariantCulture) != 0 &&
                           flags.HasFlag(flag))
            .ToList();
        if (active.Count == 0)
        {
            return string.Empty;
        }

        foreach (var flag in active)
        {
            var color = EnumColorLookup.GetColor(flag);
            if (!string.IsNullOrWhiteSpace(color))
            {
                return color;
            }
        }

        return string.Empty;
    }
}

internal static class EnumColorLookup
{
    public static string GetColor<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var name = value.ToString();
        var member = typeof(TEnum).GetField(name, BindingFlags.Public | BindingFlags.Static);
        var attr = member?.GetCustomAttribute<GriddoEnumColorAttribute>();
        return attr?.Color ?? string.Empty;
    }
}
