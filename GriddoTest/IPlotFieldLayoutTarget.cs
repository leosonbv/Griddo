using System.Collections.Generic;
using GriddoModelView;

namespace GriddoTest;

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
    bool CalibrationShowRegression { get; set; }
    bool SpectrumNormalizeIntensity { get; set; }
}
