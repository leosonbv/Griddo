namespace GriddoModelView
{
    public sealed class PlotFieldConfiguration
    {
        public int SourceFieldIndex { get; set; }
        public string TitleSelection { get; set; } = string.Empty;
    public bool ShowXAxis { get; set; } = true;
    public bool ShowYAxis { get; set; } = true;
        public string XAxis { get; set; } = string.Empty;
        public string YAxis { get; set; } = string.Empty;
        public string XAxisTitle { get; set; } = string.Empty;
        public string YAxisTitle { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string XAxisUnit { get; set; } = string.Empty;
        public string YAxisUnit { get; set; } = string.Empty;
        public int XAxisLabelPrecision { get; set; }
        public int YAxisLabelPrecision { get; set; }
    }
}
