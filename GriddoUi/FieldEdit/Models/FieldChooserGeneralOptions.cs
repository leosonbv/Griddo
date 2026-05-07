namespace GriddoUi.FieldEdit.Models;

public sealed class FieldChooserGeneralOptions
{
    public int RecordThickness { get; set; } = 24;
    public int VisibleRecordCount { get; set; }
    public bool ShowSelectionColor { get; set; } = true;
    public bool ShowCurrentCellRect { get; set; } = true;
    public bool ShowRecordSelectionColor { get; set; } = true;
    public bool ShowColSelectionColor { get; set; } = true;
    public bool ShowEditCellRect { get; set; } = true;
    public bool ShowSortingIndicators { get; set; } = true;
    public bool ShowHorizontalScrollBar { get; set; } = true;
    public bool ShowVerticalScrollBar { get; set; } = true;
    public bool IsTransposed { get; set; }
    public bool ImmediatePlottoEdit { get; set; }
    public FieldChooserGeneralOptions Clone() => new()
    {
        RecordThickness = RecordThickness,
        VisibleRecordCount = VisibleRecordCount,
        ShowSelectionColor = ShowSelectionColor,
        ShowCurrentCellRect = ShowCurrentCellRect,
        ShowRecordSelectionColor = ShowRecordSelectionColor,
        ShowColSelectionColor = ShowColSelectionColor,
        ShowEditCellRect = ShowEditCellRect,
        ShowSortingIndicators = ShowSortingIndicators,
        ShowHorizontalScrollBar = ShowHorizontalScrollBar,
        ShowVerticalScrollBar = ShowVerticalScrollBar,
        IsTransposed = IsTransposed,
        ImmediatePlottoEdit = ImmediatePlottoEdit
    };
}
