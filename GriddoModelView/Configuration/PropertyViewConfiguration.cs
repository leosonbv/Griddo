namespace GriddoModelView.Configuration;

/// <summary>
/// Globale metadata voor hoe een property van een domeinmodel
/// (bijv. HplcPeak.Concentration) weergegeven moet worden in Griddo.
/// Voor consistente chemische quantificatie.
/// </summary>
public sealed class PropertyViewConfiguration
{
    // === Identificatie ===
    public string SourceClassName { get; set; } = string.Empty;   // "HplcPeak"
    public string PropertyName { get; set; } = string.Empty;      // "Concentration"

    // === Weergave ===
    public string Header { get; set; } = string.Empty;
    public string AbbreviatedHeader { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // === Chemisch ===
    public string Unit { get; set; } = string.Empty;              // "µg/L", "min", "%", "AU·min"
    public int SignificantFigures { get; set; } = 3;
    public string Format { get; set; } = string.Empty;            // "F2", "0.000", "E3"

    // === Gedrag ===
    public string Category { get; set; } = "General";             // "Raw Data", "Results", "QC", "Identification"
    public bool IsReadOnly { get; set; } = false;
    public bool IsCalculated { get; set; } = false;

    // === Visueel (optioneel) ===
    public double DefaultWidth { get; set; } = 100.0;
}
