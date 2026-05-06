namespace GriddoModelView
{
    public sealed class FieldConfiguration
    {
        public int SourceFieldIndex { get; set; }
        public bool Fill { get; set; }
        public bool Visible { get; set; }
        public double Width { get; set; }
        public int SortPriority { get; set; }
        public bool SortAscending { get; set; }

        // Extra chemische velden (optioneel)
        public string Header { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public int SignificantFigures { get; set; } = 3;
        public string Format { get; set; } = string.Empty;
        public string Category { get; set; } = "General";
        public bool IsReadOnly { get; set; } = false;
        public bool IsCalculated { get; set; } = false;
    }
}
