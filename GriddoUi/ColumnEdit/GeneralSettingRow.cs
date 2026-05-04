namespace GriddoUi.ColumnEdit;

public enum GeneralSettingKind
{
    RowHeight,
    FillRowsVisibleCount,
    FrozenColumns,
    FrozenRows,
    ImmediatePlottoEdit,
    ShowSortingIndicators,
    ShowHorizontalScrollBar,
    ShowVerticalScrollBar,
    TransposeLayout,
    ShowSelectionColor,
    ShowRowSelectionColor,
    ShowColSelectionColor,
    ShowCurrentCellRect,
    ShowEditCellRect,
}

public enum GeneralSettingValueKind
{
    UnsignedInt,
    Boolean,
}

/// <summary>One row in the General settings property grid (label + typed value).</summary>
public sealed class GeneralSettingRow
{
    public GeneralSettingKind Setting { get; }
    public GeneralSettingValueKind ValueKind { get; }
    public string DisplayName { get; }

    public int IntValue { get; set; }
    public bool BoolValue { get; set; }

    public GeneralSettingRow(
        GeneralSettingKind setting,
        GeneralSettingValueKind valueKind,
        string displayName,
        int intValue = 0,
        bool boolValue = false)
    {
        Setting = setting;
        ValueKind = valueKind;
        DisplayName = displayName;
        IntValue = intValue;
        BoolValue = boolValue;
    }
}
