namespace Plotto.Fluent;

public sealed class PlottoChartBuilder
{
    public string XAxisTitle { get; private set; } = string.Empty;
    public string YAxisTitle { get; private set; } = string.Empty;
    public bool ShowXAxis { get; private set; } = true;
    public bool ShowYAxis { get; private set; } = true;

    public PlottoChartBuilder WithXAxis(string title, bool visible = true)
    {
        XAxisTitle = title;
        ShowXAxis = visible;
        return this;
    }

    public PlottoChartBuilder WithYAxis(string title, bool visible = true)
    {
        YAxisTitle = title;
        ShowYAxis = visible;
        return this;
    }
}
