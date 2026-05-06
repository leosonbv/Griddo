namespace GriddoUi.FieldEdit;

public enum GeneralSettingKind
{
    SectionHeader,
    RecordThickness,
    FillRecordsVisibleCount,
    FrozenFields,
    FrozenRecords,
    ImmediatePlottoEdit,
    ShowSortingIndicators,
    ShowHorizontalScrollBar,
    ShowVerticalScrollBar,
    TransposeLayout,
    ShowSelectionColor,
    ShowRecordSelectionColor,
    ShowColSelectionColor,
    ShowCurrentCellRect,
    ShowEditCellRect,
}

public enum GeneralSettingValueKind
{
    None,
    UnsignedInt,
    Boolean,
}

/// <summary>One record in the General settings property grid (label + typed value).</summary>
public sealed class GeneralSettingRecord
{
    public GeneralSettingKind Setting { get; }
    public GeneralSettingValueKind ValueKind { get; }
    public string Category { get; }
    public int CategoryLevel { get; }
    public string CategorySortKey { get; }
    public string DisplayName { get; }
    public string SettingSortKey { get; }

    public int IntValue { get; set; }
    public bool BoolValue { get; set; }

    public GeneralSettingRecord(
        GeneralSettingKind setting,
        GeneralSettingValueKind valueKind,
        string category,
        string displayName,
        int categoryLevel = 0,
        string? categorySortKey = null,
        string? settingSortKey = null,
        int intValue = 0,
        bool boolValue = false)
    {
        Setting = setting;
        ValueKind = valueKind;
        Category = category;
        CategoryLevel = categoryLevel;
        CategorySortKey = string.IsNullOrWhiteSpace(categorySortKey) ? category : categorySortKey;
        DisplayName = displayName;
        SettingSortKey = string.IsNullOrWhiteSpace(settingSortKey) ? setting.ToString() : settingSortKey;
        IntValue = intValue;
        BoolValue = boolValue;
    }
}
