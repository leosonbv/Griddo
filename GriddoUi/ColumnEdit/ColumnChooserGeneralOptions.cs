namespace GriddoUi.ColumnEdit;

public sealed class ColumnChooserGeneralOptions
{
    public int RowHeight { get; set; } = 24;
    public int VisibleRowCount { get; set; }
    public bool ShowSelectionColor { get; set; } = true;
    public bool ShowCurrentCellRect { get; set; } = true;
    public bool ShowRowSelectionColor { get; set; } = true;
    public bool ShowColSelectionColor { get; set; } = true;
    public bool ShowEditCellRect { get; set; } = true;
    public bool ShowSortingIndicators { get; set; } = true;
    public bool ShowHorizontalScrollBar { get; set; } = true;
    public bool ShowVerticalScrollBar { get; set; } = true;
    public bool ImmediatePlottoEdit { get; set; }

    public ColumnChooserGeneralOptions Clone() => new()
    {
        RowHeight = RowHeight,
        VisibleRowCount = VisibleRowCount,
        ShowSelectionColor = ShowSelectionColor,
        ShowCurrentCellRect = ShowCurrentCellRect,
        ShowRowSelectionColor = ShowRowSelectionColor,
        ShowColSelectionColor = ShowColSelectionColor,
        ShowEditCellRect = ShowEditCellRect,
        ShowSortingIndicators = ShowSortingIndicators,
        ShowHorizontalScrollBar = ShowHorizontalScrollBar,
        ShowVerticalScrollBar = ShowVerticalScrollBar,
        ImmediatePlottoEdit = ImmediatePlottoEdit
    };
}
