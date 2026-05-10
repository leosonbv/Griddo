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
    bool OverlayIstdPeaks { get; set; }
    bool OverlaySurrogatePeaks { get; set; }
    bool OverlayTargetPeaks { get; set; }
    bool CalibrationShowRegression { get; set; }
    /// <summary>When true, calibration markers show dose/HTML labels (configured on the Point labels tab).</summary>
    bool ShowCalibrationPointLabels { get; set; }
    List<PlotTitleSegmentConfiguration> CalibrationPointLabelSegments { get; set; }
    bool SpectrumNormalizeIntensity { get; set; }
}
