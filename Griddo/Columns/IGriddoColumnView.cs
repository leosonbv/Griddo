using System.Windows;
using System.Windows.Media;
using Griddo.Editing;

namespace Griddo.Columns;

public interface IGriddoColumnView
{
    string Header { get; }
    double Width { get; }
    bool Fill { get; set; }
    bool IsHtml { get; }
    TextAlignment ContentAlignment { get; }
    IGriddoCellEditor Editor { get; }
    object? GetValue(object rowSource);
    bool TrySetValue(object rowSource, object? value);
    string FormatValue(object? value);
}

public interface IGriddoSizedImageValue
{
    ImageSource GetImage(Size size);
}
