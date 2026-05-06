using System.Collections.Generic;

namespace GriddoModelView;

/// <summary>
/// Volledige configuratie van één Griddo-tabel (lay-out, frozen kolommen, etc.)
/// </summary>
public sealed class GridConfiguration
{
    public string Key { get; set; } = string.Empty;               // unieke sleutel, bijv. "HplcPeakQuant_v2"
    public string Title { get; set; } = string.Empty;

    public int FrozenColumns { get; set; } = 0;
    public int FrozenRows { get; set; } = 0;

    public bool ShowUnitsInHeader { get; set; } = true;
    public int DefaultSignificantFigures { get; set; } = 3;

    public List<ColumnConfiguration> Columns { get; set; } = new();
}