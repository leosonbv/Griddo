namespace Griddo.Hosting.Configuration;

public sealed class PlotTitleSegmentConfiguration
{
    public int SourceFieldIndex { get; set; } = -1;
    public string SourceFieldKey { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string AbbreviatedHeaderOverride { get; set; } = string.Empty;
    public bool AddLineBreakAfter { get; set; } = true;
    public bool WordWrap { get; set; } = true;
}

public sealed class PlotFieldConfiguration
{
    public int SourceFieldIndex { get; set; } = -1;
    public string SourceFieldKey { get; set; } = string.Empty;
    public string TitleSelection { get; set; } = string.Empty;
    public bool ShowTitle { get; set; } = true;
    public bool ShowXAxis { get; set; } = true;
    public bool ShowYAxis { get; set; } = true;
    public bool ShowXAxisTitle { get; set; } = true;
    public bool ShowYAxisTitle { get; set; } = true;
    public string XAxis { get; set; } = string.Empty;
    public string YAxis { get; set; } = string.Empty;
    public string XAxisTitle { get; set; } = string.Empty;
    public string YAxisTitle { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int XAxisLabelPrecision { get; set; }
    public int YAxisLabelPrecision { get; set; }
    public string XAxisLabelFormat { get; set; } = string.Empty;
    public string YAxisLabelFormat { get; set; } = string.Empty;
    public double AxisFontSize { get; set; } = 14d;
    public double TitleFontSize { get; set; } = 14d;
    public bool ChromatogramShowPeaks { get; set; }
    public bool CalibrationShowRegression { get; set; }
    public bool SpectrumNormalizeIntensity { get; set; }
    public List<PlotTitleSegmentConfiguration> TitleSegments { get; set; } = [];
}
