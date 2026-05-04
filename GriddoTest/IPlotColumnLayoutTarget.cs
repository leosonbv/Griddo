namespace GriddoTest;

public interface IPlotColumnLayoutTarget
{
    string TitleSelection { get; set; }
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
