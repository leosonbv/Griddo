namespace GriddoModelView;

/// <summary>
/// Configuratie van één kolom in een Griddo-tabel
/// </summary>
public sealed class FieldConfiguration
{
    public int SourceIndex { get; set; }
    public string Header { get; set; } = string.Empty;
    public double Width { get; set; } = 100.0;

    public string Unit { get; set; } = string.Empty;
    public int SignificantFigures { get; set; } = 3;
    public string Format { get; set; } = string.Empty;

    public string Category { get; set; } = "General";
    public bool IsReadOnly { get; set; } = false;
    public bool IsCalculated { get; set; } = false;
}