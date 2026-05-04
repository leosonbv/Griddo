using System.Globalization;
using System.Windows;
using Griddo.Editing;

namespace Griddo.Columns;

public sealed class GriddoColumnView : IGriddoColumnView, IGriddoColumnSourceMember, IGriddoColumnSourceObject, IGriddoColumnTitleView, IGriddoColumnFormatView, IGriddoColumnFontView
{
    private readonly Func<object, object?> _valueGetter;
    private readonly Func<object, object?, bool> _valueSetter;

    public GriddoColumnView(
        string header,
        double width,
        Func<object, object?> valueGetter,
        Func<object, object?, bool> valueSetter,
        IGriddoCellEditor? editor = null,
        TextAlignment? contentAlignment = null,
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
        Editor = editor ?? GriddoCellEditors.Text;
        ContentAlignment = contentAlignment ?? (Editor is GriddoNumberCellEditor ? TextAlignment.Right : TextAlignment.Left);
    }

    public string Header { get; set; }
    public string AbbreviatedHeader { get; set; } = string.Empty;
    public string FormatString { get; set; } = string.Empty;
    public string FontFamilyName { get; set; } = string.Empty;
    public double FontSize { get; set; }

    /// <inheritdoc cref="IGriddoColumnSourceMember.SourceMemberName"/>
    /// <remarks>Empty when not specified at construction; column chooser may infer from row type.</remarks>
    public string SourceMemberName { get; }
    public string SourceObjectName { get; }
    public double Width { get; }
    public bool Fill { get; set; }
    public bool IsHtml => false;
    public TextAlignment ContentAlignment { get; }
    public IGriddoCellEditor Editor { get; }

    public object? GetValue(object rowSource) => _valueGetter(rowSource);

    public bool TrySetValue(object rowSource, object? value) => _valueSetter(rowSource, value);

    public string FormatValue(object? value)
    {
        if (!string.IsNullOrWhiteSpace(FormatString) && value is IFormattable formatValue)
        {
            return formatValue.ToString(FormatString, CultureInfo.CurrentCulture);
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

public sealed class HtmlGriddoColumnView : IGriddoColumnView, IGriddoColumnSourceMember, IGriddoColumnSourceObject, IGriddoColumnTitleView, IGriddoColumnFormatView, IGriddoColumnFontView
{
    private readonly Func<object, string> _valueGetter;
    private readonly Func<object, string, bool> _valueSetter;

    public HtmlGriddoColumnView(
        string header,
        double width,
        Func<object, string> valueGetter,
        Func<object, string, bool> valueSetter,
        IGriddoCellEditor? editor = null,
        TextAlignment contentAlignment = TextAlignment.Left,
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
        Editor = editor ?? GriddoCellEditors.Text;
        ContentAlignment = contentAlignment;
    }

    public string Header { get; set; }
    public string AbbreviatedHeader { get; set; } = string.Empty;
    public string FormatString { get; set; } = string.Empty;
    public string FontFamilyName { get; set; } = string.Empty;
    public double FontSize { get; set; }

    public string SourceMemberName { get; }
    public string SourceObjectName { get; }
    public double Width { get; }
    public bool Fill { get; set; }
    public bool IsHtml => true;
    public TextAlignment ContentAlignment { get; }
    public IGriddoCellEditor Editor { get; }

    public object? GetValue(object rowSource) => _valueGetter(rowSource);

    public bool TrySetValue(object rowSource, object? value)
        => _valueSetter(rowSource, value?.ToString() ?? string.Empty);

    public string FormatValue(object? value)
    {
        if (!string.IsNullOrWhiteSpace(FormatString) && value is IFormattable formattable)
        {
            return formattable.ToString(FormatString, CultureInfo.CurrentCulture);
        }

        return value?.ToString() ?? string.Empty;
    }
}

/// <summary>
/// Boolean column: centered checkbox rendering, <see cref="GriddoCellEditors.Bool"/> for typed/F2 edits,
/// Space / second click / double-click toggle in the grid.
/// </summary>
public sealed class GriddoBoolColumnView : IGriddoColumnView, IGriddoColumnSourceMember, IGriddoColumnSourceObject, IGriddoColumnTitleView, IGriddoColumnFormatView, IGriddoColumnFontView
{
    private readonly Func<object, object?> _valueGetter;
    private readonly Func<object, object?, bool> _valueSetter;

    public GriddoBoolColumnView(
        string header,
        double width,
        Func<object, object?> valueGetter,
        Func<object, object?, bool> valueSetter,
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
        Editor = GriddoCellEditors.Bool;
        ContentAlignment = TextAlignment.Center;
    }

    public string Header { get; set; }
    public string AbbreviatedHeader { get; set; } = string.Empty;
    public string FormatString { get; set; } = string.Empty;
    public string FontFamilyName { get; set; } = string.Empty;
    public double FontSize { get; set; }

    public string SourceMemberName { get; }
    public string SourceObjectName { get; }
    public double Width { get; }
    public bool Fill { get; set; }
    public bool IsHtml => false;
    public TextAlignment ContentAlignment { get; }
    public IGriddoCellEditor Editor { get; }

    public object? GetValue(object rowSource) => _valueGetter(rowSource);

    public bool TrySetValue(object rowSource, object? value) => _valueSetter(rowSource, value);

    public string FormatValue(object? value)
    {
        if (!string.IsNullOrWhiteSpace(FormatString) && value is IFormattable formattable)
        {
            return formattable.ToString(FormatString, CultureInfo.CurrentCulture);
        }

        return value switch
        {
            null => string.Empty,
            bool b => b.ToString(CultureInfo.CurrentCulture),
            _ => bool.TryParse(value.ToString(), out var p) ? p.ToString(CultureInfo.CurrentCulture) : string.Empty
        };
    }
}
