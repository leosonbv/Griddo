using System.Collections.Generic;
using Griddo.Hosting.Configuration;

namespace GriddoModelView;

public sealed class GridConfiguration
{
    public string Key { get; set; } = string.Empty;
    public int RecordThickness { get; set; }
    public int VisibleRecordCount { get; set; }
    public int FrozenFields { get; set; }
    public int FrozenRecords { get; set; }
    public bool ShowSelectionColor { get; set; }
    public bool ShowCurrentCellRect { get; set; }
    public bool ShowRecordSelectionColor { get; set; }
    public bool ShowColSelectionColor { get; set; }
    public bool ShowEditCellRect { get; set; }
    public bool ShowSortingIndicators { get; set; }
    public bool ShowHorizontalScrollBar { get; set; }
    public bool ShowVerticalScrollBar { get; set; }
    public bool IsTransposed { get; set; }
    public bool ImmediatePlottoEdit { get; set; }
    public List<FieldConfiguration> Fields { get; set; } = new();
    public List<PlotFieldConfiguration> PlotFields { get; set; } = new();
    public List<HtmlFieldConfiguration> HtmlFields { get; set; } = new();
    public List<StabilityFieldConfiguration> StabilityFields { get; set; } = new();
}
