namespace Griddo.Hosting.Configuration;

public sealed class PlotTitleSegmentConfiguration
{
    /// <summary>Optional partition (e.g. Sample vs Quantification) for stable resolution when keys collide.</summary>
    public string SourceObjectName { get; set; } = string.Empty;

    /// <summary>Optional bound member name (same as <c>IGriddoFieldSourceMember.SourceMemberName</c>).</summary>
    public string PropertyName { get; set; } = string.Empty;

    public int SourceFieldIndex { get; set; } = -1;
    public string SourceFieldKey { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string AbbreviatedHeaderOverride { get; set; } = string.Empty;
    public bool AddLineBreakAfter { get; set; } = true;
    public bool WordWrap { get; set; } = true;
    /// <summary>When true (calibration point labels only), HTML omits the header column so only the value is shown.</summary>
    public bool OmitLabelColumn { get; set; }
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
    public double AxisFontSize { get; set; } = 21d;
    public double TitleFontSize { get; set; } = 21d;
    public bool ChromatogramShowPeaks { get; set; }
    public bool ChromatogramShowExpectedRtLine { get; set; } = true;
    public bool ChromatogramShowRtLimitLines { get; set; } = true;
    public bool ChromatogramShowSelectionCorrectedRtOnTic { get; set; } = true;
    public bool CalibrationShowRegression { get; set; }
    public bool ShowCalibrationPointLabels { get; set; } = true;
    public List<PlotTitleSegmentConfiguration> CalibrationPointLabelSegments { get; set; } = [];
    public bool SpectrumNormalizeIntensity { get; set; }
    public List<PlotTitleSegmentConfiguration> TitleSegments { get; set; } = [];
}
