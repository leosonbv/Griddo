namespace GriddoUi.ColumnEdit;

/// <summary>One editable row in <see cref="GridConfigurator"/> (maps to a property on the data shape).</summary>
public sealed class ColumnEditRow
{
    /// <summary>Original column index when built from a data grid; -1 when from CLR metadata only.</summary>
    public int SourceColumnIndex { get; init; } = -1;

    /// <summary>CLR property / member name (from <see cref="IGriddoColumnSourceMember"/> or reflection inference).</summary>
    public string PropertyName { get; init; } = string.Empty;
    public string SourceObjectName { get; init; } = string.Empty;

    /// <summary>User-facing column header text applied to <see cref="IGriddoColumnView.Header"/>.</summary>
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool Visible { get; set; } = true;

    public bool Fill { get; set; }

    public double Width { get; set; } = 140;
    public string AbbreviatedTitle { get; set; } = string.Empty;
    public string FormatString { get; set; } = string.Empty;
    public string FontFamilyName { get; set; } = string.Empty;
    public double FontSize { get; set; }
    public string FontStyleName { get; set; } = string.Empty;
    public string ForegroundColor { get; set; } = string.Empty;
    public string BackgroundColor { get; set; } = string.Empty;
    public bool IsNumericProperty { get; init; }
    public bool IsDateTimeProperty { get; init; }
    public int SortPriority { get; set; }
    public bool SortAscending { get; set; } = true;

    /// <summary>Formatted sample from the first data row, if one was supplied when the dialog was built.</summary>
    public string SampleDisplay { get; init; } = string.Empty;

    public ColumnEditRow Clone() => new()
    {
        SourceColumnIndex = SourceColumnIndex,
        PropertyName = PropertyName,
        SourceObjectName = SourceObjectName,
        Title = Title,
        Description = Description,
        Visible = Visible,
        Fill = Fill,
        Width = Width,
        AbbreviatedTitle = AbbreviatedTitle,
        FormatString = FormatString,
        FontFamilyName = FontFamilyName,
        FontSize = FontSize,
        FontStyleName = FontStyleName,
        ForegroundColor = ForegroundColor,
        BackgroundColor = BackgroundColor,
        IsNumericProperty = IsNumericProperty,
        IsDateTimeProperty = IsDateTimeProperty,
        SortPriority = SortPriority,
        SortAscending = SortAscending,
        SampleDisplay = SampleDisplay
    };
}
