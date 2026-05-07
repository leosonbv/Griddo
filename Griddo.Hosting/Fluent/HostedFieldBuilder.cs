using Griddo.Hosting.Configuration;
using Griddo.Hosting.Html;
using Griddo.Hosting.Plot;

namespace Griddo.Hosting.Fluent;

public sealed class HostedFieldBuilder
{
    public void ConfigurePlot(IPlotFieldLayoutTarget target, Action<PlotFieldConfiguration> configure)
    {
        var config = new PlotFieldConfiguration
        {
            ShowTitle = target.ShowTitle,
            ShowXAxis = target.ShowXAxis,
            ShowYAxis = target.ShowYAxis,
            ShowXAxisTitle = target.ShowXAxisTitle,
            ShowYAxisTitle = target.ShowYAxisTitle,
            XAxis = target.XAxis,
            YAxis = target.YAxis,
            XAxisTitle = target.XAxisTitle,
            YAxisTitle = target.YAxisTitle,
            Label = target.Label,
            XAxisLabelPrecision = target.XAxisLabelPrecision,
            YAxisLabelPrecision = target.YAxisLabelPrecision,
            XAxisLabelFormat = target.XAxisLabelFormat,
            YAxisLabelFormat = target.YAxisLabelFormat,
            AxisFontSize = target.AxisFontSize,
            TitleFontSize = target.TitleFontSize
        };
        configure(config);
        target.ShowTitle = config.ShowTitle;
        target.ShowXAxis = config.ShowXAxis;
        target.ShowYAxis = config.ShowYAxis;
        target.ShowXAxisTitle = config.ShowXAxisTitle;
        target.ShowYAxisTitle = config.ShowYAxisTitle;
        target.XAxis = config.XAxis;
        target.YAxis = config.YAxis;
        target.XAxisTitle = config.XAxisTitle;
        target.YAxisTitle = config.YAxisTitle;
        target.Label = config.Label;
        target.XAxisLabelPrecision = config.XAxisLabelPrecision;
        target.YAxisLabelPrecision = config.YAxisLabelPrecision;
        target.XAxisLabelFormat = config.XAxisLabelFormat;
        target.YAxisLabelFormat = config.YAxisLabelFormat;
        target.AxisFontSize = config.AxisFontSize;
        target.TitleFontSize = config.TitleFontSize;
    }

    public void ConfigureHtml(IHtmlFieldLayoutTarget target, Action<HtmlFieldConfiguration> configure)
    {
        var config = new HtmlFieldConfiguration
        {
            IsCategoryField = target.IsCategoryField,
            FontFamilyName = target.FontFamilyName,
            FontSize = target.FontSize,
            FontStyleName = target.FontStyleName,
            Segments = target.Segments
        };
        configure(config);
        target.IsCategoryField = config.IsCategoryField;
        target.FontFamilyName = config.FontFamilyName;
        target.FontSize = config.FontSize;
        target.FontStyleName = config.FontStyleName;
        target.Segments = config.Segments;
    }

    public void ConfigureStability(IStabilityFieldLayoutTarget target, Action<StabilityFieldConfiguration> configure)
    {
        var config = new StabilityFieldConfiguration
        {
            Label = target.Label,
            Series = target.Series.Select(s => new StabilitySeriesConfiguration
            {
                SourceFieldIndex = s.SourceFieldIndex,
                Enabled = s.Enabled,
                ShowSdLines = s.ShowSdLines,
                ShowLine = s.ShowLine,
                ShowMarker = s.ShowMarker,
                Color = s.Color,
                AxisSide = s.AxisSide
            }).ToList()
        };
        configure(config);
        target.Label = config.Label;
        target.Series = config.Series;
    }
}
