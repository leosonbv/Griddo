using Griddo.Hosting.Configuration;

namespace Griddo.Hosting.Plot;

public interface IPlotFieldLayoutTarget
{
    string PlotTypeKey { get; }
    string TitleSelection { get; set; }
    bool ShowTitle { get; set; }
    List<PlotTitleSegmentConfiguration> TitleSegments { get; set; }
    bool ShowXAxis { get; set; }
    bool ShowYAxis { get; set; }
    bool ShowXAxisTitle { get; set; }
    bool ShowYAxisTitle { get; set; }
    string XAxis { get; set; }
    string YAxis { get; set; }
    string XAxisTitle { get; set; }
    string YAxisTitle { get; set; }
    string Label { get; set; }
    int XAxisLabelPrecision { get; set; }
    int YAxisLabelPrecision { get; set; }
    string XAxisLabelFormat { get; set; }
    string YAxisLabelFormat { get; set; }
    double AxisFontSize { get; set; }
    double TitleFontSize { get; set; }
    bool ChromatogramShowPeaks { get; set; }
    /// <summary>Chromatogram: dashed vertical line at corrected expected retention time.</summary>
    bool ChromatogramShowExpectedRtLine { get; set; }
    /// <summary>Chromatogram: light dashed lines at ±TimeWindow/2 around corrected expected RT.</summary>
    bool ChromatogramShowRtLimitLines { get; set; }
    /// <summary>Sample TIC: show corrected expected RT lines for compounds in compound selection (and current row).</summary>
    bool ChromatogramShowSelectionCorrectedRtOnTic { get; set; }
    bool OverlayIstdPeaks { get; set; }
    bool OverlaySurrogatePeaks { get; set; }
    bool OverlayTargetPeaks { get; set; }
    bool CalibrationShowRegression { get; set; }
    /// <summary>When true, calibration plots show HTML point labels and connector lines from markers to labels (content is configured on the Point labels tab).</summary>
    bool ShowCalibrationPointLabels { get; set; }
    List<PlotTitleSegmentConfiguration> CalibrationPointLabelSegments { get; set; }
    bool SpectrumNormalizeIntensity { get; set; }
}
