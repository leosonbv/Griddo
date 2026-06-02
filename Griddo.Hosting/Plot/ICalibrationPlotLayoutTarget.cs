using Griddo.Hosting.Configuration;

namespace Griddo.Hosting.Plot;

/// <summary>
/// Calibration plot specific layout options (regression, point labels).
/// Segregated per ISP.
/// </summary>
public interface ICalibrationPlotLayoutTarget
{
    bool CalibrationShowRegression { get; set; }
    /// <summary>When true, calibration plots show HTML point labels and connector lines from markers to labels (content is configured on the Point labels tab).</summary>
    bool ShowCalibrationPointLabels { get; set; }
    List<PlotTitleSegmentConfiguration> CalibrationPointLabelSegments { get; set; }

    /// <summary>Calibration point label font size; 0 = default (13).</summary>
    double CalibrationPointLabelFontSize { get; set; }
}
