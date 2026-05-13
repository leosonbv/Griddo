using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Windows;
using Griddo.Editing;

namespace Griddo.Fields;

public sealed class GriddoFieldView : IGriddoFieldView, IGriddoFieldSourceMember, IGriddoFieldSourceObject, IGriddoFieldTitleView, IGriddoFieldDescriptionView, IGriddoFieldFormatView, IGriddoFieldFontView, IGriddoFieldColorView, IGriddoCheckboxToggleFieldView, IGriddoFieldWrapView, IGriddoFieldAlignmentView
{
    private readonly Func<object, object?> _valueGetter;
    private readonly Func<object, object?, bool> _valueSetter;
    private readonly bool _forcedCheckboxFromMemberTypes;
    private PropertyInfo? _cachedBooleanPropertyForNamedSource;

    public GriddoFieldView(
        string header,
        double width,
        Func<object, object?> valueGetter,
        Func<object, object?, bool> valueSetter,
        IGriddoCellEditor? editor = null,
        TextAlignment? contentAlignment = null,
        int fieldFill = 0,
        string? sourceMemberName = null,
        string? sourceObjectName = null,
        IReadOnlyList<Type>? inferBooleanCheckboxFromMemberTypes = null)
    {
        Header = header;
        Width = width;
        FieldFill = fieldFill;
        SourceMemberName = sourceMemberName ?? string.Empty;
        SourceObjectName = sourceObjectName ?? string.Empty;
        _valueGetter = valueGetter;
        _valueSetter = valueSetter;

        _forcedCheckboxFromMemberTypes = TryFindBooleanProperty(
            inferBooleanCheckboxFromMemberTypes,
            SourceMemberName,
            out _);

        IGriddoCellEditor resolvedEditor = editor ?? GriddoCellEditors.Text;
        var resolvedAlignment = contentAlignment;
        if (_forcedCheckboxFromMemberTypes)
        {
            resolvedEditor = GriddoCellEditors.Bool;
            resolvedAlignment ??= TextAlignment.Center;
        }

        Editor = resolvedEditor;
        ContentAlignment = resolvedAlignment ?? (Editor is GriddoNumberCellEditor ? TextAlignment.Right : TextAlignment.Left);
    }

    private static bool TryFindBooleanProperty(
        IReadOnlyList<Type>? declaringTypes,
        string memberName,
        out PropertyInfo? property)
    {
        property = null;
        if (declaringTypes is null
            || declaringTypes.Count == 0
            || string.IsNullOrEmpty(memberName))
        {
            return false;
        }

        foreach (var t in declaringTypes)
        {
            var p = t.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (p is null)
            {
                continue;
            }

            var underlying = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            if (underlying == typeof(bool))
            {
                property = p;
                return true;
            }
        }

        return false;
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

    /// <inheritdoc cref="IGriddoFieldSourceMember.SourceMemberName"/>
    /// <remarks>Empty when not specified at construction; field chooser may infer from record type.</remarks>
    public string SourceMemberName { get; }
    public string SourceObjectName { get; }
    public double Width { get; }
    public int FieldFill { get; set; }
    public bool IsHtml => false;
    public TextAlignment ContentAlignment { get; set; }
    public IGriddoCellEditor Editor { get; private set; }

    public bool IsCheckboxCell(object recordSource)
    {
        if (_forcedCheckboxFromMemberTypes)
        {
            return true;
        }

        if (_valueGetter(recordSource) is bool)
        {
            return true;
        }

        if (TryResolveNamedSourceBooleanProperty(recordSource, out var prop) && prop is not null)
        {
            return true;
        }

        if (string.IsNullOrEmpty(SourceObjectName)
            && !string.IsNullOrEmpty(SourceMemberName))
        {
            var direct = recordSource.GetType().GetProperty(
                SourceMemberName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (direct is not null)
            {
                var underlying = Nullable.GetUnderlyingType(direct.PropertyType) ?? direct.PropertyType;
                if (underlying == typeof(bool))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryResolveNamedSourceBooleanProperty(object recordSource, out PropertyInfo? property)
    {
        property = _cachedBooleanPropertyForNamedSource;
        if (property is not null)
        {
            return true;
        }

        if (string.IsNullOrEmpty(SourceObjectName) || string.IsNullOrEmpty(SourceMemberName))
        {
            return false;
        }

        if (!GriddoNamedSourceFields.TryGetNamedSource(recordSource, SourceObjectName, out var source) || source is null)
        {
            return false;
        }

        property = source.GetType().GetProperty(
            SourceMemberName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (property is null)
        {
            return false;
        }

        var underlying = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (underlying != typeof(bool))
        {
            property = null;
            return false;
        }

        _cachedBooleanPropertyForNamedSource = property;
        return true;
    }

    public object? GetValue(object recordSource) => _valueGetter(recordSource);

    public bool TrySetValue(object recordSource, object? value) => _valueSetter(recordSource, value);

    public string FormatValue(object? value)
    {
        if (!string.IsNullOrWhiteSpace(FormatString) && value is IFormattable formatValue)
        {
            try
            {
                return formatValue.ToString(FormatString, CultureInfo.CurrentCulture);
            }
            catch (FormatException)
            {
                // Invalid format string for this value type: fall back to default formatting.
            }
        }

        return value switch
        {
            null => string.Empty,
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture),
            _ => value.ToString() ?? string.Empty
        };
    }
}

public sealed class HtmlGriddoFieldView : IGriddoFieldView, IGriddoFieldSourceMember, IGriddoFieldSourceObject, IGriddoFieldTitleView, IGriddoFieldDescriptionView, IGriddoFieldFormatView, IGriddoFieldFontView, IGriddoFieldColorView, IGriddoFieldWrapView, IGriddoFieldAlignmentView
{
    private readonly Func<object, string> _valueGetter;
    private readonly Func<object, string, bool> _valueSetter;

    public HtmlGriddoFieldView(
        string header,
        double width,
        Func<object, string> valueGetter,
        Func<object, string, bool> valueSetter,
        IGriddoCellEditor? editor = null,
        TextAlignment contentAlignment = TextAlignment.Left,
        int fieldFill = 0,
        string? sourceMemberName = null,
        string? sourceObjectName = null)
    {
        Header = header;
        Width = width;
        FieldFill = fieldFill;
        SourceMemberName = sourceMemberName ?? string.Empty;
        SourceObjectName = sourceObjectName ?? string.Empty;
        _valueGetter = valueGetter;
        _valueSetter = valueSetter;
        Editor = editor ?? GriddoCellEditors.Text;
        ContentAlignment = contentAlignment;
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
    public int FieldFill { get; set; }
    public bool IsHtml => true;
    public TextAlignment ContentAlignment { get; set; }
    public IGriddoCellEditor Editor { get; }

    public object? GetValue(object recordSource) => _valueGetter(recordSource);

    public bool TrySetValue(object recordSource, object? value)
        => _valueSetter(recordSource, value?.ToString() ?? string.Empty);

    public string FormatValue(object? value)
    {
        if (!string.IsNullOrWhiteSpace(FormatString) && value is IFormattable formattable)
        {
            try
            {
                return formattable.ToString(FormatString, CultureInfo.CurrentCulture);
            }
            catch (FormatException)
            {
                // Invalid format string for this value type: fall back to default formatting.
            }
        }

        return value?.ToString() ?? string.Empty;
    }
}

/// <summary>
/// Boolean field: centered checkbox rendering, <see cref="GriddoCellEditors.Bool"/> for typed/F2 edits,
/// Space / second click / double-click toggle in the grid.
/// </summary>
public sealed class GriddoBoolFieldView : IGriddoFieldView, IGriddoFieldSourceMember, IGriddoFieldSourceObject, IGriddoFieldTitleView, IGriddoFieldDescriptionView, IGriddoFieldFormatView, IGriddoFieldFontView, IGriddoFieldColorView, IGriddoCheckboxToggleFieldView, IGriddoFieldWrapView, IGriddoFieldAlignmentView
{
    private readonly Func<object, object?> _valueGetter;
    private readonly Func<object, object?, bool> _valueSetter;
    private readonly Func<object, bool>? _isCheckboxCell;

    public GriddoBoolFieldView(
        string header,
        double width,
        Func<object, object?> valueGetter,
        Func<object, object?, bool> valueSetter,
        int fieldFill = 0,
        string? sourceMemberName = null,
        string? sourceObjectName = null,
        Func<object, bool>? isCheckboxCell = null)
    {
        Header = header;
        Width = width;
        FieldFill = fieldFill;
        SourceMemberName = sourceMemberName ?? string.Empty;
        SourceObjectName = sourceObjectName ?? string.Empty;
        _valueGetter = valueGetter;
        _valueSetter = valueSetter;
        _isCheckboxCell = isCheckboxCell;
        Editor = GriddoCellEditors.Bool;
        ContentAlignment = TextAlignment.Center;
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
    public int FieldFill { get; set; }
    public bool IsHtml => false;
    public TextAlignment ContentAlignment { get; set; }
    public IGriddoCellEditor Editor { get; }

    public object? GetValue(object recordSource) => _valueGetter(recordSource);

    public bool TrySetValue(object recordSource, object? value) => _valueSetter(recordSource, value);

    public string FormatValue(object? value)
    {
        if (!string.IsNullOrWhiteSpace(FormatString) && value is IFormattable formattable)
        {
            try
            {
                return formattable.ToString(FormatString, CultureInfo.CurrentCulture);
            }
            catch (FormatException)
            {
                // Invalid format string for this value type: fall back to default formatting.
            }
        }

        return value switch
        {
            null => string.Empty,
            bool b => b.ToString(CultureInfo.CurrentCulture),
            _ => bool.TryParse(value.ToString(), out var p) ? p.ToString(CultureInfo.CurrentCulture) : string.Empty
        };
    }

    public bool IsCheckboxCell(object recordSource) => _isCheckboxCell?.Invoke(recordSource) ?? true;
}
