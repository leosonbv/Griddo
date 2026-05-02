using System.Windows;
using System.Windows.Media;

namespace Griddo;

public interface IGriddoColumnView
{
    string Header { get; }
    double Width { get; }
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
