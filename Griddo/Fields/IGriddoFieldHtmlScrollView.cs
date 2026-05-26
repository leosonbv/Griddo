namespace Griddo.Fields;

/// <summary>
/// Optional HTML field contract for per-cell vertical scrolling when content exceeds the cell height.
/// </summary>
public interface IGriddoFieldHtmlScrollView
{
    bool AutoVerticalScrollBar { get; }
}
