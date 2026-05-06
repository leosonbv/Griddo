namespace GriddoTest;

public interface IPlotFieldLayoutTarget
{
    string TitleSelection { get; set; }
    bool ShowXAxis { get; set; }
    bool ShowYAxis { get; set; }
    string XAxis { get; set; }
    string YAxis { get; set; }
    string XAxisTitle { get; set; }
    string YAxisTitle { get; set; }
    string Label { get; set; }
    string XAxisUnit { get; set; }
    string YAxisUnit { get; set; }
    int XAxisLabelPrecision { get; set; }
    int YAxisLabelPrecision { get; set; }
}
