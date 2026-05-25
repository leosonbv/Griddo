using Griddo.Hosting.Configuration;

namespace Griddo.Hosting.Plot;

/// <summary>
/// Common layout and presentation settings shared by all plot types hosted in Griddo fields.
/// Segregated per ISP (Interface Segregation Principle).
/// </summary>
public interface IPlotLayoutTarget
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
}
