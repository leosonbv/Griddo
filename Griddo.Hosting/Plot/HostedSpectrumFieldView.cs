using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Reflection;
using Griddo.Editing;
using Griddo.Fields;
using Griddo.Hosting.Abstractions;
using Griddo.Hosting.Configuration;
using Plotto.Charting.Controls;
using Plotto.Charting.Core;

namespace Griddo.Hosting.Plot;

public sealed class HostedSpectrumFieldView : IGriddoHostedFieldView, IGriddoFieldSourceMember, IGriddoFieldSourceObject, IGriddoFieldDescriptionView, IPlotFieldLayoutTarget
{
    private readonly ISpectrumSignalProvider _signalProvider;
    private readonly Func<IReadOnlyList<IGriddoFieldView>>? _allFieldsAccessor;

    public HostedSpectrumFieldView(
        string header,
        double width,
        ISpectrumSignalProvider signalProvider,
        Func<IReadOnlyList<IGriddoFieldView>>? allFieldsAccessor = null,
        string sourceObjectName = "",
        string sourceMemberName = "")
    {
        Header = header;
        Width = width;
        _signalProvider = signalProvider;
        _allFieldsAccessor = allFieldsAccessor;
        SourceObjectName = sourceObjectName;
        SourceMemberName = sourceMemberName;
        Editor = GriddoCellEditors.Text;
        ContentAlignment = TextAlignment.Left;
    }

    public string Header { get; set; }
    public string Description { get; set; } = string.Empty;
    public string PlotTypeKey => "Spectrum";
    public string TitleSelection { get; set; } = "Spectrum";
    public bool ShowTitle { get; set; } = true;
    public List<PlotTitleSegmentConfiguration> TitleSegments { get; set; } = [];
    public bool ShowXAxis { get; set; } = true;
    public bool ShowYAxis { get; set; } = true;
    public bool ShowXAxisTitle { get; set; } = true;
    public bool ShowYAxisTitle { get; set; } = true;
    public string XAxis { get; set; } = string.Empty;
    public string YAxis { get; set; } = string.Empty;
    public string XAxisTitle { get; set; } = "m/z";
    public string YAxisTitle { get; set; } = "Intensity";
    public string Label { get; set; } = string.Empty;
    public int XAxisLabelPrecision { get; set; } = 2;
    public int YAxisLabelPrecision { get; set; } = 2;
    public string XAxisLabelFormat { get; set; } = string.Empty;
    public string YAxisLabelFormat { get; set; } = string.Empty;
    public double AxisFontSize { get; set; } = 10d;
    public double TitleFontSize { get; set; } = 11d;
    public bool ChromatogramShowPeaks { get; set; }
    public bool OverlayIstdPeaks { get; set; }
    public bool OverlaySurrogatePeaks { get; set; }
    public bool OverlayTargetPeaks { get; set; }
    public bool CalibrationShowRegression { get; set; }
    public bool SpectrumNormalizeIntensity { get; set; }

    public string SourceObjectName { get; }
    public string SourceMemberName { get; }
    public double Width { get; }
    public bool Fill { get; set; }
    public bool IsHtml => false;
    public TextAlignment ContentAlignment { get; }
    public IGriddoCellEditor Editor { get; }

    public object? GetValue(object recordSource) => null;
    public bool TrySetValue(object recordSource, object? value) => false;
    public string FormatValue(object? value) => string.Empty;

    public FrameworkElement CreateHostElement()
    {
        var chart = CreateChart();
        ApplyChartSettings(chart, null);
        return new Border { Background = null, Child = chart, SnapsToDevicePixels = true, IsHitTestVisible = true };
    }

    public void UpdateHostElement(FrameworkElement host, object recordSource, bool isSelected, bool isCurrentCell)
    {
        if (host is not Border { Child: SpectrumControl chart })
        {
            return;
        }

        var points = _signalProvider.GetPoints(recordSource);
        var pointsValue = BuildRuntimePointsValue(points);
        chart.SetValue(SkiaChartBaseControl.PointsProperty, pointsValue);
        ApplyChartSettings(chart, recordSource);
    }

    public bool IsHostInEditMode(FrameworkElement host) =>
        host is Border { Child: SpectrumControl chart } && chart.RenderMode == ChartRenderMode.Editor;

    public void SetHostEditMode(FrameworkElement host, bool isEditing)
    {
        if (host is Border { Child: SpectrumControl chart })
        {
            chart.RenderMode = isEditing ? ChartRenderMode.Editor : ChartRenderMode.Renderer;
            chart.DeferRendererActivationToParent = false;
            chart.IsHitTestVisible = true;
            chart.Focus();
        }
    }

    public bool TryHandleHostedMouseWheel(FrameworkElement host, MouseWheelEventArgs e) =>
        host is Border { Child: SkiaChartBaseControl chart } && chart.ApplyWheelZoomFromRoute(e);

    public void SyncHostedUiScale(FrameworkElement host, double contentScale)
    {
        if (host is Border { Child: SkiaChartBaseControl chart })
        {
            chart.UiScale = contentScale;
        }
    }

    public bool TryGetClipboardHtmlFragment(
        FrameworkElement? host,
        object recordSource,
        int cellWidthPx,
        int cellHeightPx,
        out string htmlFragment)
    {
        htmlFragment = string.Empty;
        if (host is not Border { Child: SkiaChartBaseControl chart })
        {
            return false;
        }

        var w = Math.Max(120, cellWidthPx);
        var h = Math.Max(80, cellHeightPx);
        var png = chart.GetChartPngBytes(w, h);
        var b64 = Convert.ToBase64String(png);
        htmlFragment = $"<img src=\"data:image/png;base64,{b64}\" alt=\"\" width=\"{w}\" height=\"{h}\" />";
        return true;
    }

    public void ApplyPlotDirectEditOption(FrameworkElement host, bool gridUsesHostedPlotDirectMouseDown)
    {
        if (host is Border { Child: SpectrumControl chart })
        {
            chart.DeferRendererActivationToParent = gridUsesHostedPlotDirectMouseDown;
        }
    }

    private static SpectrumControl CreateChart() =>
        new()
        {
            RequireActivationClick = false,
            RenderMode = ChartRenderMode.Renderer,
            EnableInlineEditing = true,
            EnableMouseInteractions = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = true
        };

    private void ApplyChartSettings(SkiaChartBaseControl chart, object? recordSource)
    {
        chart.ChartTitle = PlotTitleHtmlBuilder.BuildTitleHtml(recordSource, _allFieldsAccessor, TitleSegments);
        chart.ShowChartTitle = ShowTitle;
        chart.AxisLabelX = ShowXAxisTitle ? XAxisTitle : string.Empty;
        chart.AxisLabelY = ShowYAxisTitle ? YAxisTitle : string.Empty;
        chart.ChartLabel = Label;
        chart.AxisLabelPrecisionX = Math.Clamp(XAxisLabelPrecision, 0, 10);
        chart.AxisLabelPrecisionY = Math.Clamp(YAxisLabelPrecision, 0, 10);
        chart.AxisLabelFormatX = XAxisLabelFormat ?? string.Empty;
        chart.AxisLabelFormatY = YAxisLabelFormat ?? string.Empty;
        chart.AxisFontSize = AxisFontSize;
        chart.TitleFontSize = TitleFontSize;
        chart.ShowXAxis = ShowXAxis;
        chart.ShowYAxis = ShowYAxis;
    }

    private static object BuildRuntimePointsValue(IReadOnlyList<SignalPoint> points)
    {
        var propertyType = SkiaChartBaseControl.PointsProperty.PropertyType;
        var pointType = propertyType.IsGenericType
            ? propertyType.GetGenericArguments()[0]
            : typeof(ChartPoint);
        var ctor = pointType.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, [typeof(double), typeof(double)], null);
        if (ctor is null)
        {
            return Array.Empty<ChartPoint>();
        }

        var array = Array.CreateInstance(pointType, points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            array.SetValue(ctor.Invoke([points[i].X, points[i].Y]), i);
        }

        return array;
    }
}
