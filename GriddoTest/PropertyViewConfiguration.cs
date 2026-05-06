using System;

namespace GriddoModelView
{
    /// <summary>
    /// Globale metadata voor hoe een property van een domeinmodel weergegeven moet worden in Griddo.
    /// Volledig compatibel met chemische quantificatie + bestaande GriddoTest functionaliteit.
    /// </summary>
    public sealed class PropertyViewConfiguration
    {
        // Identificatie
        public string SourceClassName { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;

        // Weergave (bestaande + nieuwe)
        public string Header { get; set; } = string.Empty;
        public string AbbreviatedHeader { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        // Formaat (beide namen ondersteund voor compatibiliteit)
        public string StringFormat { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;

        // Chemisch
        public string Unit { get; set; } = string.Empty;
        public int SignificantFigures { get; set; } = 3;

        // Visueel (bestaande)
        public double FontSize { get; set; }
        public string FontStyle { get; set; } = string.Empty;
        public string ForegroundColor { get; set; } = string.Empty;
        public string BackgroundColor { get; set; } = string.Empty;

        // Gedrag & categorie (nieuw)
        public string Category { get; set; } = "General";
        public bool IsReadOnly { get; set; } = false;
        public bool IsCalculated { get; set; } = false;
        public double DefaultWidth { get; set; } = 100.0;
    }
}
