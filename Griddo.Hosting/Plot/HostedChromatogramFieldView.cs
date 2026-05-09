using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Linq;
using System.Reflection;
using Griddo.Editing;
using Griddo.Fields;
using Griddo.Hosting.Abstractions;
using Griddo.Hosting.Configuration;
using Plotto.Charting.Controls;
using Plotto.Charting.Core;

namespace Griddo.Hosting.Plot;

public sealed class HostedChromatogramFieldView : IGriddoHostedFieldView, IGriddoFieldSourceMember, IGriddoFieldSourceObject, IGriddoFieldDescriptionView, IPlotFieldLayoutTarget
{
    private readonly IChromatogramSignalProvider _signalProvider;
    private readonly Func<IReadOnlyList<IGriddoFieldView>>? _allFieldsAccessor;

    public HostedChromatogramFieldView(
        string header,
        double width,
        IChromatogramSignalProvider signalProvider,
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
    public string PlotTypeKey => "Chromatogram";
    public string TitleSelection { get; set; } = "Chromatogram";
    public bool ShowTitle { get; set; } = true;
    public List<PlotTitleSegmentConfiguration> TitleSegments { get; set; } = [];
    public bool ShowXAxis { get; set; } = true;
    public bool ShowYAxis { get; set; } = true;
    public bool ShowXAxisTitle { get; set; } = true;
    public bool ShowYAxisTitle { get; set; } = true;
    public string XAxis { get; set; } = string.Empty;
    public string YAxis { get; set; } = string.Empty;
    public string XAxisTitle { get; set; } = "Time";
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
    public Color SelectedPeakOverlayColor { get; set; } = Color.FromRgb(22, 163, 74);
    public Color AlternativePeakOverlayColor { get; set; } = Color.FromRgb(234, 179, 8);
    public Color IntegrationLineOverlayColor { get; set; } = Color.FromRgb(255, 0, 0);
    public Color ManualIntegrationFillColor { get; set; } = Color.FromRgb(26, 118, 218);
    public double OverlayLineWidth { get; set; } = 1.5d;
    public int PeakFillAlpha { get; set; } = 48;
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
        chart.IntegrationChanged += OnChartIntegrationChanged;
        chart.PeakSplitRequested += OnChartPeakSplitRequested;
        chart.PeakSelectionRequested += OnChartPeakSelectionRequested;
        ApplyChartSettings(chart, null);
        return Wrap(chart);
    }

    public void UpdateHostElement(FrameworkElement host, object recordSource, bool isSelected, bool isCurrentCell)
    {
        if (host is not Border { Child: ChromatogramControl chart })
        {
            return;
        }

        // Avoid rebinding series every frame; Griddo can call this during render loops.
        // Rebinding while dragging in editor mode resets manual integration interaction state.
        if (!ReferenceEquals(host.Tag, recordSource))
        {
            var points = _signalProvider.GetPoints(recordSource);
            var pointsValue = BuildRuntimePointsValue(points);
            chart.SetValue(SkiaChartBaseControl.PointsProperty, pointsValue);
            host.Tag = recordSource;
        }

        ApplyChartSettings(chart, recordSource);
        SyncPeakOverlay(chart, recordSource);
    }

    public bool IsHostInEditMode(FrameworkElement host) =>
        host is Border { Child: ChromatogramControl chart } && chart.RenderMode == ChartRenderMode.Editor;

    public void SetHostEditMode(FrameworkElement host, bool isEditing)
    {
        if (host is Border { Child: ChromatogramControl chart })
        {
            chart.RenderMode = isEditing ? ChartRenderMode.Editor : ChartRenderMode.Renderer;
            // In edit mode (F2/single/double), route pointer gestures directly to Plotto.
            chart.DeferRendererActivationToParent = false;
            chart.IsHitTestVisible = true;
            chart.Focus();
            SyncPeakOverlay(chart, host.Tag);
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
        if (host is Border { Child: ChromatogramControl chart })
        {
            chart.DeferRendererActivationToParent = gridUsesHostedPlotDirectMouseDown;
        }
    }

    public bool ShouldRelayLeftDoubleClickWhileInHostedEditMode() => true;

    public void RelayDirectEditMouseDown(FrameworkElement host, MouseButtonEventArgs eFromGrid)
    {
        if (host is not Border { Child: ChromatogramControl chart })
        {
            return;
        }

        if (eFromGrid is { ChangedButton: MouseButton.Left, ClickCount: 2 })
        {
            chart.Focus();
            chart.ZoomOutCompletely();
            return;
        }

        if (eFromGrid is { ChangedButton: MouseButton.Right, ClickCount: 2 })
        {
            chart.Focus();
            chart.ZoomOutCompletely();
            return;
        }

        host.UpdateLayout();
        chart.UpdateLayout();
        chart.ResetHitTestGeometrySync();

        var routed = new MouseButtonEventArgs(eFromGrid.MouseDevice, eFromGrid.Timestamp, eFromGrid.ChangedButton)
        {
            RoutedEvent = Mouse.MouseDownEvent,
            Source = chart
        };
        chart.Focus();
        chart.RaiseEvent(routed);
    }

    public void RelayDirectEditMouseUp(FrameworkElement host, MouseButtonEventArgs eFromGrid)
    {
        if (host is not Border { Child: ChromatogramControl chart })
        {
            return;
        }

        var routed = new MouseButtonEventArgs(eFromGrid.MouseDevice, eFromGrid.Timestamp, eFromGrid.ChangedButton)
        {
            RoutedEvent = Mouse.MouseUpEvent,
            Source = chart
        };
        chart.Focus();
        chart.RaiseEvent(routed);
    }

    private Border Wrap(ChromatogramControl chart) =>
        new()
        {
            Background = null,
            Child = chart,
            SnapsToDevicePixels = true,
            IsHitTestVisible = true
        };

    private void OnChartIntegrationChanged(object? sender, IntegrationRegionEventArgs e)
    {
        if (sender is not ChromatogramControl chart)
        {
            return;
        }

        if (chart.Parent is not Border host || host.Tag is null)
        {
            return;
        }

        if (!_signalProvider.TryApplyManualIntegration(host.Tag, e.Region))
        {
            return;
        }

        // Manual integration changes peak boundaries/selection immediately; refresh live overlays in editor.
        SyncPeakOverlay(chart, host.Tag);
    }

    private void OnChartPeakSplitRequested(object? sender, PeakSplitEventArgs e)
    {
        if (sender is not ChromatogramControl chart)
        {
            return;
        }

        if (chart.Parent is not Border host || host.Tag is null)
        {
            return;
        }

        if (!_signalProvider.TryApplyPeakSplit(host.Tag, e.SplitX))
        {
            return;
        }

        SyncPeakOverlay(chart, host.Tag);
    }

    private void OnChartPeakSelectionRequested(object? sender, PeakSelectionEventArgs e)
    {
        if (sender is not ChromatogramControl chart)
        {
            return;
        }

        if (chart.Parent is not Border host || host.Tag is null)
        {
            return;
        }

        if (!_signalProvider.TrySelectPeakAtX(host.Tag, e.X))
        {
            return;
        }

        SyncPeakOverlay(chart, host.Tag);
    }

    private static ChromatogramControl CreateChart() =>
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
        if (chart is ChromatogramControl chromatogram)
        {
            chromatogram.SetPeakOverlayColors(
                new SkiaSharp.SKColor(SelectedPeakOverlayColor.R, SelectedPeakOverlayColor.G, SelectedPeakOverlayColor.B, SelectedPeakOverlayColor.A),
                new SkiaSharp.SKColor(AlternativePeakOverlayColor.R, AlternativePeakOverlayColor.G, AlternativePeakOverlayColor.B, AlternativePeakOverlayColor.A),
                new SkiaSharp.SKColor(IntegrationLineOverlayColor.R, IntegrationLineOverlayColor.G, IntegrationLineOverlayColor.B, IntegrationLineOverlayColor.A),
                new SkiaSharp.SKColor(ManualIntegrationFillColor.R, ManualIntegrationFillColor.G, ManualIntegrationFillColor.B, ManualIntegrationFillColor.A),
                OverlayLineWidth,
                PeakFillAlpha);
        }
    }

    private void SyncPeakOverlay(ChromatogramControl chart, object? recordSource)
    {
        if (_signalProvider is ITicPeakOverlayOptions ticOptions)
        {
            ticOptions.OverlayIstdPeaks = OverlayIstdPeaks;
            ticOptions.OverlaySurrogatePeaks = OverlaySurrogatePeaks;
            ticOptions.OverlayTargetPeaks = OverlayTargetPeaks;
        }

        if (recordSource is null)
        {
            chart.IntegrationRegions = Array.Empty<IntegrationRegion>();
            chart.AlternativeIntegrationRegions = Array.Empty<IntegrationRegion>();
            chart.IntegrationRegionsManualIntegrated = Array.Empty<bool>();
            chart.AlternativeIntegrationRegionsManualIntegrated = Array.Empty<bool>();
            chart.ColoredIntegrationRegions = Array.Empty<ColoredIntegrationRegion>();
            return;
        }

        chart.ColoredIntegrationRegions = _signalProvider.GetPeakOverlayRegionsColored(recordSource);
        if (!ChromatogramShowPeaks)
        {
            chart.IntegrationRegions = Array.Empty<IntegrationRegion>();
            chart.AlternativeIntegrationRegions = Array.Empty<IntegrationRegion>();
            chart.IntegrationRegionsManualIntegrated = Array.Empty<bool>();
            chart.AlternativeIntegrationRegionsManualIntegrated = Array.Empty<bool>();
            return;
        }

        var overlays = _signalProvider.GetPeakOverlayRegionsWithSelection(recordSource);
        static (double Begin, double End) RegionKey(IntegrationRegion r) =>
            (Math.Min(r.Start.X, r.End.X), Math.Max(r.Start.X, r.End.X));

        var selectedOverlays = overlays.Where(static x => x.IsSelected).ToList();
        var selectedKeys = selectedOverlays
            .Select(static x => RegionKey(x.Region))
            .ToHashSet();

        chart.IntegrationRegions = selectedOverlays.Select(static x => x.Region).ToList();
        chart.IntegrationRegionsManualIntegrated = selectedOverlays.Select(static x => x.IsManualIntegrated).ToList();

        var alternativeOverlays = overlays
            .Where(static x => !x.IsSelected)
            .Where(x => !selectedKeys.Contains(RegionKey(x.Region)))
            .ToList();
        chart.AlternativeIntegrationRegions = alternativeOverlays.Select(static x => x.Region).ToList();
        chart.AlternativeIntegrationRegionsManualIntegrated = alternativeOverlays.Select(static x => x.IsManualIntegrated).ToList();
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
