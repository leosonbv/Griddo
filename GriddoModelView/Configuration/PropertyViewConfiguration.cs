namespace GriddoModelView;

public sealed class PropertyViewConfiguration
{
    public string SourceClassName { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
    public string Header { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string StringFormat { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public int SignificantFigures { get; set; } = 3;
    public double FontSize { get; set; }
    public string FontStyle { get; set; } = string.Empty;
    public bool NoWrap { get; set; }
    public string ForegroundColor { get; set; } = string.Empty;
    public string BackgroundColor { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public bool IsReadOnly { get; set; }
    public bool IsCalculated { get; set; }
    public double DefaultWidth { get; set; } = 100.0;
}
