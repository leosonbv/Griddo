using System.Globalization;
using System.Windows;

namespace Griddo;

public sealed class GriddoColumnView : IGriddoColumnView
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
        bool fill = false)
    {
        Header = header;
        Width = width;
        Fill = fill;
        _valueGetter = valueGetter;
        _valueSetter = valueSetter;
        Editor = editor ?? GriddoCellEditors.Text;
        ContentAlignment = contentAlignment ?? (Editor is GriddoNumberCellEditor ? TextAlignment.Right : TextAlignment.Left);
    }

    public string Header { get; }
    public double Width { get; }
    public bool Fill { get; set; }
    public bool IsHtml => false;
    public TextAlignment ContentAlignment { get; }
    public IGriddoCellEditor Editor { get; }

    public object? GetValue(object rowSource) => _valueGetter(rowSource);

    public bool TrySetValue(object rowSource, object? value) => _valueSetter(rowSource, value);

    public string FormatValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture),
            _ => value.ToString() ?? string.Empty
        };
    }
}

public sealed class HtmlGriddoColumnView : IGriddoColumnView
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
        bool fill = false)
    {
        Header = header;
        Width = width;
        Fill = fill;
        _valueGetter = valueGetter;
        _valueSetter = valueSetter;
        Editor = editor ?? GriddoCellEditors.Text;
        ContentAlignment = contentAlignment;
    }

    public string Header { get; }
    public double Width { get; }
    public bool Fill { get; set; }
    public bool IsHtml => true;
    public TextAlignment ContentAlignment { get; }
    public IGriddoCellEditor Editor { get; }

    public object? GetValue(object rowSource) => _valueGetter(rowSource);

    public bool TrySetValue(object rowSource, object? value)
        => _valueSetter(rowSource, value?.ToString() ?? string.Empty);

    public string FormatValue(object? value) => value?.ToString() ?? string.Empty;
}
