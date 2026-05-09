using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Griddo.Editing;
using Griddo.Fields;
using Griddo.Hosting.Abstractions;
using Griddo.Hosting.Configuration;
using Plotto.Charting.Controls;
using Plotto.Charting.Core;

namespace Griddo.Hosting.Plot;

public sealed class HostedCalibrationFieldView : IGriddoHostedFieldView, IGriddoFieldSourceMember, IGriddoFieldSourceObject, IGriddoFieldDescriptionView, IPlotFieldLayoutTarget
{
    private readonly ICalibrationSignalProvider _signalProvider;
    private readonly Func<IReadOnlyList<IGriddoFieldView>>? _allFieldsAccessor;

    public HostedCalibrationFieldView(
        string header,
        double width,
        ICalibrationSignalProvider signalProvider,
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
    public string PlotTypeKey => "Calibration curve";
    public string TitleSelection { get; set; } = "Calibration curve";
    public bool ShowTitle { get; set; } = true;
    public List<PlotTitleSegmentConfiguration> TitleSegments { get; set; } = [];
    public bool ShowXAxis { get; set; } = true;
    public bool ShowYAxis { get; set; } = true;
    public bool ShowXAxisTitle { get; set; } = true;
    public bool ShowYAxisTitle { get; set; } = true;
    public string XAxis { get; set; } = string.Empty;
    public string YAxis { get; set; } = string.Empty;
    public string XAxisTitle { get; set; } = "Concentration";
    public string YAxisTitle { get; set; } = "Response";
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

    public Func<object?, string?>? ViewportZoomRecordKey { get; set; }

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
        var border = new Border { Background = null, Child = chart, SnapsToDevicePixels = true, IsHitTestVisible = true };
        var plotKey = HostedPlotViewportMemory.PlotKey(SourceMemberName, Header);
        chart.ViewportChanged += (_, _) =>
        {
            if (border.Tag is { } row)
            {
                HostedPlotViewportMemory.Remember(row, plotKey, chart.Viewport, ViewportZoomRecordKey);
            }
        };
        return border;
    }

    public void UpdateHostElement(FrameworkElement host, object recordSource, bool isSelected, bool isCurrentCell)
    {
        if (host is not Border { Child: CalibrationCurveControl chart })
        {
            return;
        }

        var plotKey = HostedPlotViewportMemory.PlotKey(SourceMemberName, Header);
        var previousRow = host.Tag;
        var recordChanged = !ReferenceEquals(previousRow, recordSource);

        if (recordChanged)
        {
            HostedPlotViewportMemory.SaveLeavingRow(previousRow, plotKey, chart.Viewport, ViewportZoomRecordKey);
            host.Tag = recordSource;
            chart.CalibrationPoints = _signalProvider.GetPoints(recordSource)
                .Select(static p => new CalibrationPoint { X = p.X, Y = p.Y, IsEnabled = p.Enabled })
                .ToList();
            chart.FitMode = _signalProvider.GetFitMode(recordSource);
        }

        ApplyChartSettings(chart, recordSource);

        if (recordChanged)
        {
            HostedPlotViewportMemory.TryRestore(recordSource, plotKey, chart, ViewportZoomRecordKey);
            HostedPlotViewportMemory.ScheduleDeferredTryRestore(host, recordSource, plotKey, chart, ViewportZoomRecordKey);
        }
    }

    public bool IsHostInEditMode(FrameworkElement host) =>
        host is Border { Child: CalibrationCurveControl chart } && chart.RenderMode == ChartRenderMode.Editor;

    public void SetHostEditMode(FrameworkElement host, bool isEditing)
    {
        if (host is Border { Child: CalibrationCurveControl chart })
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
        if (host is Border { Child: CalibrationCurveControl chart })
        {
            chart.DeferRendererActivationToParent = gridUsesHostedPlotDirectMouseDown;
        }
    }

    private static CalibrationCurveControl CreateChart() =>
        new()
        {
            RequireActivationClick = false,
            RenderMode = ChartRenderMode.Renderer,
            EnableInlineEditing = true,
            EnableMouseInteractions = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = true,
            FitMode = CalibrationFitMode.Linear,
            CalibrationPoints = []
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
}
