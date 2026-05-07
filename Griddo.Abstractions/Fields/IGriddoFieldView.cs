using System.Windows;
using System.Windows.Media;
using Griddo.Editing;

namespace Griddo.Fields;

public interface IGriddoFieldView
{
    string Header { get; set; }
    double Width { get; }
    bool Fill { get; set; }
    bool IsHtml { get; }
    TextAlignment ContentAlignment { get; }
    IGriddoCellEditor Editor { get; }
    object? GetValue(object recordSource);
    bool TrySetValue(object recordSource, object? value);
    string FormatValue(object? value);
}

public interface IGriddoFieldTitleView
{
    string AbbreviatedHeader { get; set; }
}

public interface IGriddoFieldDescriptionView
{
    string Description { get; set; }
}

public interface IGriddoFieldSortValueView
{
    object? GetSortValue(object recordSource);
}

public interface IGriddoFieldFormatView
{
    string FormatString { get; set; }
}

public interface IGriddoFieldFontView
{
    string FontFamilyName { get; set; }
    double FontSize { get; set; }
    string FontStyleName { get; set; }
}

public interface IGriddoFieldColorView
{
    string ForegroundColor { get; set; }
    string BackgroundColor { get; set; }
}

public interface IGriddoDynamicFieldColorView
{
    string GetForegroundColor(object recordSource);
    string GetBackgroundColor(object recordSource);
}

public interface IGriddoSizedImageValue
{
    ImageSource GetImage(Size size);
}
