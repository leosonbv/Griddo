using Griddo.Hosting.Fluent;
using Griddo.Hosting.Html;
using Griddo.Hosting.Plot;
using Griddo.Fields;
using Plotto.Fluent;

namespace GriddoTest.Integration;

internal static class QuantoCompositionSample
{
    public static IReadOnlyList<IGriddoFieldView> BuildFields(
        IEnumerable<IGriddoFieldView> baseFields,
        IPlotFieldLayoutTarget plotTarget,
        IHtmlFieldLayoutTarget htmlTarget,
        IStabilityFieldLayoutTarget stabilityTarget)
    {
        var gridBuilder = new GriddoBuilder().AddFields(baseFields);

        var plotBuilder = new PlottoChartBuilder()
            .WithXAxis("Acquisition Time")
            .WithYAxis("Signal");

        var hosted = new HostedFieldBuilder();
        hosted.ConfigurePlot(plotTarget, cfg =>
        {
            cfg.ShowXAxis = plotBuilder.ShowXAxis;
            cfg.ShowYAxis = plotBuilder.ShowYAxis;
            cfg.XAxisTitle = plotBuilder.XAxisTitle;
            cfg.YAxisTitle = plotBuilder.YAxisTitle;
        });
        hosted.ConfigureHtml(htmlTarget, cfg => cfg.IsCategoryField = true);
        hosted.ConfigureStability(stabilityTarget, cfg => cfg.Label = "Stability");

        return gridBuilder.BuildFields();
    }
}
