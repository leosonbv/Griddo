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
    public double AxisFontSize { get; set; } = 15d;
    public double TitleFontSize { get; set; } = 16.5d;
    public bool ChromatogramShowPeaks { get; set; }
    public bool ChromatogramShowExpectedRtLine { get; set; }
    public bool ChromatogramShowRtLimitLines { get; set; }
    public bool ChromatogramShowSelectionCorrectedRtOnTic { get; set; }
    public bool OverlayIstdPeaks { get; set; }
    public bool OverlaySurrogatePeaks { get; set; }
    public bool OverlayTargetPeaks { get; set; }
    public bool CalibrationShowRegression { get; set; }
    public bool ShowCalibrationPointLabels { get; set; } = true;
    public List<PlotTitleSegmentConfiguration> CalibrationPointLabelSegments { get; set; } = [];
    public bool SpectrumNormalizeIntensity { get; set; }

    /// <summary>
    /// When set, calibration point-label segments resolve against this field list (e.g. method <c>ResponseView</c> columns).
    /// Defaults to the grid registry when null.
    /// </summary>
    public Func<IReadOnlyList<IGriddoFieldView>>? CalibrationPointLabelFieldsAccessor { get; set; }

    public Func<object?, string?>? ViewportZoomRecordKey { get; set; }

    public string SourceObjectName { get; }
    public string SourceMemberName { get; }
    public double Width { get; }
    public int FieldFill { get; set; }
    public bool IsHtml => false;
    public TextAlignment ContentAlignment { get; }
    public IGriddoCellEditor Editor { get; }

    public object? GetValue(object recordSource) => null;
    public bool TrySetValue(object recordSource, object? value) => false;
    public string FormatValue(object? value) => string.Empty;

    public FrameworkElement CreateHostElement()
    {
        var chart = CreateChart();
        chart.CalibrationPointToggled += OnCalibrationPointToggled;
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
        }

        ApplyChartSettings(chart, recordSource);

        // Rebind points + curve whenever this cell is updated: the row object may be unchanged while bracket
        // data mutates (e.g. Enabled toggled from another grid column). Title uses ApplyChartSettings above
        // and already reflected that; skipping rebind here left the regression line stale.
        // Viewport save/restore stays tied to logical row changes only (HostedChromatogramFieldView pattern).
        if (recordSource is null)
        {
            return;
        }

        if (recordChanged)
        {
            var willHavePoints = _signalProvider.GetPoints(recordSource).Count > 0;
            chart.BeginSuppressCalibrationViewportFit();
            try
            {
                chart.SuppressAutomaticViewportFitOnNextPointsChange = willHavePoints;
                BindCalibrationSeries(chart, recordSource);
                ApplyCurveOverlay(chart, recordSource);
                ApplyCurrentQuantifierGuide(chart, recordSource);
                var restored = HostedPlotViewportMemory.TryRestore(recordSource, plotKey, chart, ViewportZoomRecordKey);
                if (!restored && chart.Points.Count > 0)
                {
                    chart.FitViewportToCalibrationData();
                }
            }
            finally
            {
                chart.EndSuppressCalibrationViewportFit();
            }

            HostedPlotViewportMemory.ScheduleDeferredTryRestore(host, recordSource, plotKey, chart, ViewportZoomRecordKey);
        }
        else
        {
            chart.BeginSuppressCalibrationViewportFit();
            try
            {
                chart.SuppressAutomaticViewportFitOnNextPointsChange = chart.Points.Count > 0;
                BindCalibrationSeries(chart, recordSource);
                ApplyCurveOverlay(chart, recordSource);
                ApplyCurrentQuantifierGuide(chart, recordSource);
            }
            finally
            {
                chart.EndSuppressCalibrationViewportFit();
            }
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

    public bool ShouldRelayLeftDoubleClickWhileInHostedEditMode() => true;

    public void RelayDirectEditMouseDown(FrameworkElement host, MouseButtonEventArgs eFromGrid)
    {
        if (host is not Border { Child: CalibrationCurveControl chart })
        {
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
        if (host is not Border { Child: CalibrationCurveControl chart })
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
            CalibrationPoints = [],
            ShowCalibrationPointLabels = true,
            CalibrationPointLabelFontSize = 13.5d
        };

    private void OnCalibrationPointToggled(object? sender, CalibrationPointEventArgs e)
    {
        if (sender is not CalibrationCurveControl chart || chart.Parent is not Border host || host.Tag is null)
        {
            return;
        }

        var recordSource = host.Tag;
        if (!_signalProvider.TryToggleCalibrationPoint(recordSource, e.Point.X, e.Point.Y, e.Point.IsEnabled))
        {
            e.Point.IsEnabled = !e.Point.IsEnabled;
            chart.InvalidateVisual();
            return;
        }

        chart.BeginSuppressCalibrationViewportFit();
        try
        {
            chart.SuppressAutomaticViewportFitOnNextPointsChange = chart.Points.Count > 0;
            BindCalibrationSeries(chart, recordSource);
            ApplyCurveOverlay(chart, recordSource);
            ApplyCurrentQuantifierGuide(chart, recordSource);
        }
        finally
        {
            chart.EndSuppressCalibrationViewportFit();
        }
    }

    private void BindCalibrationSeries(CalibrationCurveControl chart, object recordSource)
    {
        var raw = _signalProvider.GetPoints(recordSource);
        var fitMode = _signalProvider.GetFitMode(recordSource);

        if (TryReuseCalibrationPoints(chart, raw, fitMode))
        {
            return;
        }

        List<CalibrationPoint> points = [];
        for (var i = 0; i < raw.Count; i++)
        {
            var p = raw[i];
            string plain;
            if (ShowCalibrationPointLabels)
            {
                if (CalibrationPointLabelSegments.Count > 0 && CalibrationPointLabelSegments.Exists(static s => s.Enabled))
                {
                    var html = PlotTitleHtmlBuilder.BuildCalibrationPointLabelHtml(
                        recordSource,
                        i,
                        _allFieldsAccessor,
                        CalibrationPointLabelSegments,
                        _signalProvider,
                        CalibrationPointLabelFieldsAccessor);
                    plain = PlotTitleHtmlBuilder.HtmlTableToPlainSummary(html);
                }
                else
                {
                    plain = p.DefaultLabel ?? string.Empty;
                }
            }
            else
            {
                plain = string.Empty;
            }

            points.Add(new CalibrationPoint
            {
                X = p.X,
                Y = p.Y,
                IsEnabled = p.Enabled,
                LabelPlainText = plain,
                PointKind = p.PointKind,
                AllowEnabledToggle = p.AllowEnabledToggle
            });
        }

        chart.CalibrationPoints = points;
        chart.FitMode = fitMode;
    }

    private bool TryReuseCalibrationPoints(
        CalibrationCurveControl chart,
        IReadOnlyList<CalibrationSignalPoint> raw,
        CalibrationFitMode fitMode)
    {
        var existing = chart.CalibrationPoints;
        if (existing.Count != raw.Count)
        {
            return false;
        }

        for (var i = 0; i < raw.Count; i++)
        {
            var p = raw[i];
            var e = existing[i];
            if (p.X != e.X || p.Y != e.Y || p.Enabled != e.IsEnabled || p.PointKind != e.PointKind)
            {
                return false;
            }
        }

        if (chart.FitMode != fitMode)
        {
            chart.FitMode = fitMode;
        }

        var useRichLabels = ShowCalibrationPointLabels
            && CalibrationPointLabelSegments.Count > 0
            && CalibrationPointLabelSegments.Exists(static s => s.Enabled);

        if (useRichLabels)
        {
            return false;
        }

        if (!ShowCalibrationPointLabels)
        {
            for (var i = 0; i < raw.Count; i++)
            {
                if (!string.IsNullOrEmpty(existing[i].LabelPlainText))
                {
                    return false;
                }
            }

            return true;
        }

        for (var i = 0; i < raw.Count; i++)
        {
            var expected = raw[i].DefaultLabel ?? string.Empty;
            if (!string.Equals(existing[i].LabelPlainText ?? string.Empty, expected, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void ApplyCurveOverlay(CalibrationCurveControl chart, object recordSource)
    {
        var samples = _signalProvider.GetCurveLineSamples(recordSource, 160);
        chart.CurveOverlayPoints = samples is not { Count: >= 2 }
            ? null
            : samples.Select(static p => new ChartPoint(p.X, p.Y)).ToArray();
    }

    private void ApplyCurrentQuantifierGuide(CalibrationCurveControl chart, object recordSource)
    {
        if (_signalProvider.TryGetCurrentQuantifierGuide(recordSource, out var x, out var y))
        {
            chart.CurrentQuantifierGuideX = x;
            chart.CurrentQuantifierGuideY = y;
            return;
        }

        chart.CurrentQuantifierGuideX = double.NaN;
        chart.CurrentQuantifierGuideY = double.NaN;
    }

    private void ApplyChartSettings(SkiaChartBaseControl chart, object? recordSource)
    {
        var xTitle = XAxisTitle;
        var yTitle = YAxisTitle;
        if (recordSource is not null
            && _signalProvider.TryGetAxisTitles(recordSource, out var providerXTitle, out var providerYTitle))
        {
            if (!string.IsNullOrWhiteSpace(providerXTitle))
            {
                xTitle = providerXTitle;
            }

            if (!string.IsNullOrWhiteSpace(providerYTitle))
            {
                yTitle = providerYTitle;
            }
        }

        chart.ChartTitle = PlotTitleHtmlBuilder.BuildTitleHtml(recordSource, _allFieldsAccessor, TitleSegments);
        chart.ShowChartTitle = ShowTitle;
        chart.AxisLabelX = ShowXAxisTitle ? xTitle : string.Empty;
        chart.AxisLabelY = ShowYAxisTitle ? yTitle : string.Empty;
        chart.ChartLabel = Label;
        chart.AxisLabelPrecisionX = Math.Clamp(XAxisLabelPrecision, 0, 10);
        chart.AxisLabelPrecisionY = Math.Clamp(YAxisLabelPrecision, 0, 10);
        chart.AxisLabelFormatX = XAxisLabelFormat ?? string.Empty;
        chart.AxisLabelFormatY = YAxisLabelFormat ?? string.Empty;
        chart.AxisFontSize = AxisFontSize;
        chart.TitleFontSize = TitleFontSize;
        chart.ShowXAxis = ShowXAxis;
        chart.ShowYAxis = ShowYAxis;
        if (chart is CalibrationCurveControl cal)
        {
            cal.ShowCalibrationPointLabels = ShowCalibrationPointLabels;
            cal.CalibrationPointLabelFontSize = Math.Clamp(AxisFontSize * 0.85, 6d, 22d);
        }
    }
}
