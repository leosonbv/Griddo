namespace GriddoModelView;

public sealed class GridLayoutDefinition
{
    public string GridKey { get; set; } = string.Empty;
    public int VisibleRowCount { get; set; }
    public int FrozenColumns { get; set; }
    public int FrozenRows { get; set; }
    public bool ShowSelectionColor { get; set; } = true;
    public bool ShowCurrentCellRect { get; set; } = true;
    public bool ShowRowSelectionColor { get; set; } = true;
    public bool ShowColSelectionColor { get; set; } = true;
    public bool ShowEditCellRect { get; set; } = true;
    public bool ShowSortingIndicators { get; set; } = true;
    public bool ShowHorizontalScrollBar { get; set; } = true;
    public bool ShowVerticalScrollBar { get; set; } = true;
    public bool ImmediatePlottoEdit { get; set; }
    public List<GridColumnLayoutDefinition> Columns { get; set; } = [];
    public List<GridPlotColumnLayoutDefinition> PlotColumns { get; set; } = [];
}

public sealed class GridColumnLayoutDefinition
{
    public int SourceColumnIndex { get; set; } = -1;
    public bool Fill { get; set; }
    public bool Visible { get; set; } = true;
    public double Width { get; set; } = 140;
    public int SortPriority { get; set; }
    public bool SortAscending { get; set; } = true;
}

public sealed class GridPlotColumnLayoutDefinition
{
    public int SourceColumnIndex { get; set; } = -1;
    public string TitleSelection { get; set; } = string.Empty;
    public string XAxis { get; set; } = string.Empty;
    public string YAxis { get; set; } = string.Empty;
    public string XAxisTitle { get; set; } = string.Empty;
    public string YAxisTitle { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string XAxisUnit { get; set; } = string.Empty;
    public string YAxisUnit { get; set; } = string.Empty;
    public int XAxisLabelPrecision { get; set; } = 2;
    public int YAxisLabelPrecision { get; set; } = 2;
}
