using System.Windows;
using System.Windows.Input;
using SkiaSharp;
using Plotto.Charting.Axes;
using Plotto.Charting.Core;
using Plotto.Charting.Geometry;
using Plotto.Charting.Rendering;

namespace Plotto.Charting.Controls;

public class CalibrationCurveControl : SkiaChartBaseControl
{
    private const int FitSamplingSegments = 160;

    private readonly SKPaint _pointEnabledPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = new SKColor(220, 55, 55)
    };

    private readonly SKPaint _pointDisabledPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = new SKColor(160, 45, 45, 180)
    };

    private readonly SKPaint _fitPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 2f,
        Color = new SKColor(65, 135, 225)
    };

    private readonly SKPaint _pointStrokePaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1f,
        Color = new SKColor(140, 30, 30)
    };

    private readonly SKPaint _originGuidePaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1f,
        Color = new SKColor(150, 150, 150, 220)
    };

    public IReadOnlyList<CalibrationPoint> CalibrationPoints
    {
        get => (IReadOnlyList<CalibrationPoint>)GetValue(CalibrationPointsProperty);
        set => SetValue(CalibrationPointsProperty, value);
    }

    public static readonly DependencyProperty CalibrationPointsProperty =
        DependencyProperty.Register(
            nameof(CalibrationPoints),
            typeof(IReadOnlyList<CalibrationPoint>),
            typeof(CalibrationCurveControl),
            new FrameworkPropertyMetadata(
                Array.Empty<CalibrationPoint>(),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnCalibrationChanged,
                CoerceCalibrationPoints));

    private static object CoerceCalibrationPoints(DependencyObject d, object baseValue)
    {
        return baseValue ?? Array.Empty<CalibrationPoint>();
    }

    public CalibrationFitMode FitMode
    {
        get => (CalibrationFitMode)GetValue(FitModeProperty);
        set => SetValue(FitModeProperty, value);
    }

    public static readonly DependencyProperty FitModeProperty =
        DependencyProperty.Register(
            nameof(FitMode),
            typeof(CalibrationFitMode),
            typeof(CalibrationCurveControl),
            new FrameworkPropertyMetadata(CalibrationFitMode.Linear, FrameworkPropertyMetadataOptions.AffectsRender, OnFitModeChanged));

    public event EventHandler<CalibrationPointEventArgs>? CalibrationPointToggled;

    /// <summary>
    /// Y is derived from the fit at the X window, so wheel zoom must change X (Ctrl+wheel: Y for no-fit / edge cases).
    /// </summary>
    protected override void ApplyUiScaleToResources()
    {
        base.ApplyUiScaleToResources();
        var s = PlotUiScale;
        _fitPaint.StrokeWidth = 2f * s;
        _pointStrokePaint.StrokeWidth = Math.Max(0.5f, 1f * s);
        _originGuidePaint.StrokeWidth = Math.Max(0.5f, 1f * s);
    }

    public override bool ApplyWheelZoomFromRoute(MouseWheelEventArgs e)
    {
        if (!CanUseScrollWheelZoom())
        {
            return false;
        }

        var pivot = e.GetPosition(this);
        var factor = e.Delta > 0 ? 0.9 : 1.1;
        var mod = Keyboard.Modifiers;
        var ctrlWithoutShift = mod.HasFlag(ModifierKeys.Control) && !mod.HasFlag(ModifierKeys.Shift);
        if (ctrlWithoutShift)
        {
            ZoomYAt(pivot, factor);
        }
        else
        {
            ZoomXAt(pivot, factor);
        }

        return true;
    }

    /// <summary>
    /// Only the outer zoom-out box limits X. With a fit, Y is not taken from the prior viewport (no inner Y clamp);
    /// it is set from the curve at the current X edges so the segment fills bottom-left to top-right (+5% pad), then clipped to global Y limits.
    /// </summary>
    protected override void ApplyViewportInteractionClamp()
    {
        if (CalibrationPoints.Count == 0 ||
            !CalibrationViewportBounds.TryGetZoomOutExtents(CalibrationPoints, FitMode, FitSamplingSegments, out var xMinLim, out var xMaxLim, out var yMinLim, out var yMaxLim))
        {
            base.ApplyViewportInteractionClamp();
            return;
        }

        if (IsViewportClampAfterRectZoom)
        {
            var rxMin = Viewport.XMin;
            var rxMax = Viewport.XMax;
            var ryMin = Viewport.YMin;
            var ryMax = Viewport.YMax;
            IntersectWindowWithOuterBounds(ref rxMin, ref rxMax, xMinLim, xMaxLim);
            IntersectWindowWithOuterBounds(ref ryMin, ref ryMax, yMinLim, yMaxLim);
            Viewport.XMin = rxMin;
            Viewport.XMax = rxMax;
            Viewport.YMin = ryMin;
            Viewport.YMax = ryMax;
            Viewport.EnsureMinimumSize();
            return;
        }

        var xMin = Viewport.XMin;
        var xMax = Viewport.XMax;
        ClampWindowToBounds(ref xMin, ref xMax, xMinLim, xMaxLim);

        var enabled = CalibrationPoints.Where(p => p.IsEnabled).ToArray();
        double yMin;
        double yMax;
        if (CalibrationFitSolver.TryCreateEvaluator(FitMode, enabled, out var eval))
        {
            var y0 = eval(xMin);
            var y1 = eval(xMax);
            if (double.IsFinite(y0) && double.IsFinite(y1))
            {
                var cLo = Math.Min(y0, y1);
                var cHi = Math.Max(y0, y1);
                var dy = Math.Max(1e-9, cHi - cLo);
                var pad = dy * 0.05;
                yMin = cLo - pad;
                yMax = cHi + pad;
                ClampWindowToBounds(ref yMin, ref yMax, yMinLim, yMaxLim);
            }
            else
            {
                yMin = Viewport.YMin;
                yMax = Viewport.YMax;
                ClampWindowToBounds(ref yMin, ref yMax, yMinLim, yMaxLim);
            }
        }
        else
        {
            yMin = Viewport.YMin;
            yMax = Viewport.YMax;
            ClampWindowToBounds(ref yMin, ref yMax, yMinLim, yMaxLim);
        }

        Viewport.XMin = xMin;
        Viewport.XMax = xMax;
        Viewport.YMin = yMin;
        Viewport.YMax = yMax;
        Viewport.EnsureMinimumSize();
    }

    /// <summary>
    /// Keep viewport inside outer zoom-out box by intersecting intervals only (no shift toward origin when zoomed in).
    /// </summary>
    private static void IntersectWindowWithOuterBounds(ref double min, ref double max, double boundMin, double boundMax)
    {
        const double eps = 1e-12;
        min = Math.Max(min, boundMin);
        max = Math.Min(max, boundMax);
        if (max - min >= eps)
        {
            return;
        }

        var span = Math.Max(1e-9, (boundMax - boundMin) * 0.01);
        min = boundMin;
        max = Math.Min(boundMax, boundMin + span);
        if (max - min < eps)
        {
            max = boundMax;
            min = Math.Max(boundMin, boundMax - span);
        }
    }

    private static void ClampWindowToBounds(ref double min, ref double max, double boundMin, double boundMax)
    {
        var span = max - min;
        var limitSpan = boundMax - boundMin;
        if (limitSpan <= 1e-15)
        {
            min = boundMin;
            max = boundMax;
            return;
        }

        if (span >= limitSpan - 1e-12)
        {
            min = boundMin;
            max = boundMax;
            return;
        }

        if (max > boundMax)
        {
            max = boundMax;
            min = max - span;
        }

        if (min < boundMin)
        {
            min = boundMin;
            max = min + span;
        }

        if (max > boundMax)
        {
            max = boundMax;
            min = Math.Max(boundMin, max - span);
        }
    }


    protected override void OnChartMouseUp(ChartPoint point, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || CalibrationPoints.Count == 0)
        {
            return;
        }

        var nearest = FindNearest(point, 0.03);
        if (nearest is null)
        {
            return;
        }

        nearest.IsEnabled = !nearest.IsEnabled;
        CalibrationPointToggled?.Invoke(this, new CalibrationPointEventArgs(nearest));
        RequestRender();
    }

    protected override void DrawSeries(SKCanvas canvas, IReadOnlyList<ChartPoint> points, SKRect plotRect)
    {
        var enabled = CalibrationPoints.Where(p => p.IsEnabled).ToArray();
        if (!CalibrationFitSolver.TryCreateEvaluator(FitMode, enabled, out var eval))
        {
            return;
        }

        ChartSkiaSampledFunctionPath.DrawPolyline(
            canvas,
            plotRect,
            Viewport.XMin,
            Viewport.XMax,
            FitSamplingSegments,
            eval,
            ToPixelX,
            ToPixelY,
            _fitPaint);
    }

    protected override void DrawAxes(SKCanvas canvas, SKRect plotRect)
    {
        if (UseSparklineLayout || (!ShowXAxis && !ShowYAxis))
        {
            return;
        }

        const double eps = 1e-12;
        // x=0 lies left of the plot: show y-axis spine. y=0 lies below the plot bottom: show x-axis spine.
        var showYAxisLine = ShowYAxis && Viewport.XMin > eps;
        var showXAxisLine = ShowXAxis && Viewport.YMin > eps;
        if (showYAxisLine)
        {
            canvas.DrawLine(plotRect.Left, plotRect.Top, plotRect.Left, plotRect.Bottom, AxisStrokePaint);
        }

        if (showXAxisLine)
        {
            canvas.DrawLine(plotRect.Left, plotRect.Bottom, plotRect.Right, plotRect.Bottom, AxisStrokePaint);
        }

        var zs = PlotUiScale;
        var axOff = ChartPlotLayout.AxisLabelInsetFromPlotLeft(zs);
        var axisMetrics = AxisFont.Metrics;
        var axisFontHeight = Math.Max(1f, -axisMetrics.Ascent + axisMetrics.Descent);
        var xTickTop = plotRect.Bottom + (axisFontHeight * 0.2f);
        var xTickBaseline = xTickTop - axisMetrics.Ascent;
        var xTickLength = Math.Max(3f * zs, axisFontHeight * 0.35f);
        var xAxisReserveY = ShowXAxis
            ? ChartPlotLayout.ComputeXAxisReserveY(zs, AxisFontSize, !string.IsNullOrWhiteSpace(AxisLabelX))
            : 0f;
        var yTickRight = plotRect.Left - Math.Max(axOff, axisFontHeight * 0.5f);
        var yTickLength = Math.Max(3f * zs, axisFontHeight * 0.35f);

        if (ShowXAxis)
        {
            var minGap = 0f;
            var maxXTickCount = Math.Clamp((int)(plotRect.Width / Math.Max(10f * zs, axisFontHeight * 0.9f)), 2, 120);
            var bestXTickRequest = 2;
            var xTicks = NiceAxisTickGrid.Generate(Viewport.XMin, Viewport.XMax, bestXTickRequest);
            for (var candidateTickCount = 3; candidateTickCount <= maxXTickCount; candidateTickCount++)
            {
                var candidateTicks = NiceAxisTickGrid.Generate(Viewport.XMin, Viewport.XMax, candidateTickCount, out var candStep);
                var lastRight = float.NegativeInfinity;
                var overlaps = false;
                foreach (var candidateTick in candidateTicks)
                {
                    if (!NonNegativeAxisTickPolicy.AllowsLabel(candidateTick))
                    {
                        continue;
                    }

                    var x = ToPixelX(candidateTick, plotRect);
                    var label = AxisTickLabelFormatter.FormatSnappedToGrid(candidateTick, candStep, AxisLabelPrecisionX, null, AxisLabelFormatX);
                    var width = AxisFont.MeasureText(label);
                    var left = x - (width * 0.5f);
                    left = Math.Clamp(left, plotRect.Left, plotRect.Right - width);
                    if (left < lastRight + minGap)
                    {
                        overlaps = true;
                        break;
                    }

                    lastRight = left + width;
                }

                if (overlaps)
                {
                    break;
                }

                xTicks = candidateTicks;
                bestXTickRequest = candidateTickCount;
            }

            xTicks = NiceAxisTickGrid.Generate(Viewport.XMin, Viewport.XMax, bestXTickRequest, out var xStep);
            var lastLabelRight = float.NegativeInfinity;
            foreach (var tick in xTicks)
            {
                if (!NonNegativeAxisTickPolicy.AllowsLabel(tick))
                {
                    continue;
                }

                var x = ToPixelX(tick, plotRect);
                canvas.DrawLine(x, plotRect.Bottom, x, plotRect.Bottom + xTickLength, AxisStrokePaint);
                var label = AxisTickLabelFormatter.FormatSnappedToGrid(tick, xStep, AxisLabelPrecisionX, null, AxisLabelFormatX);
                var width = AxisFont.MeasureText(label);
                var left = x - (width * 0.5f);
                left = Math.Clamp(left, plotRect.Left, plotRect.Right - width);
                var right = left + width;
                if (left < lastLabelRight + minGap)
                {
                    continue;
                }

                canvas.DrawText(label, left, xTickBaseline, SKTextAlign.Left, AxisFont, AxisLabelPaint);
                lastLabelRight = right;
            }
        }

        if (ShowYAxis)
        {
            var yMinGap = 3f * zs;
            var maxYTickCount = Math.Clamp((int)(plotRect.Height / Math.Max(10f * zs, axisFontHeight * 0.9f)), 2, 120);
            var bestYTickRequest = 2;
            var yTicks = NiceAxisTickGrid.Generate(Viewport.YMin, Viewport.YMax, bestYTickRequest);
            for (var candidateTickCount = 3; candidateTickCount <= maxYTickCount; candidateTickCount++)
            {
                var candidateTicks = NiceAxisTickGrid.Generate(Viewport.YMin, Viewport.YMax, candidateTickCount, out _);
                var lastTopProbe = float.PositiveInfinity;
                var overlaps = false;
                foreach (var candidateTick in candidateTicks)
                {
                    if (!NonNegativeAxisTickPolicy.AllowsLabel(candidateTick))
                    {
                        continue;
                    }

                    var py = ToPixelY(candidateTick, plotRect);
                    var baselineProbe = py - (axisMetrics.Ascent + axisMetrics.Descent) * 0.5f;
                    var topProbe = baselineProbe + axisMetrics.Ascent;
                    var bottomProbe = baselineProbe + axisMetrics.Descent;
                    if (bottomProbe >= lastTopProbe - yMinGap)
                    {
                        overlaps = true;
                        break;
                    }

                    lastTopProbe = topProbe;
                }

                if (overlaps)
                {
                    break;
                }

                yTicks = candidateTicks;
                bestYTickRequest = candidateTickCount;
            }

            yTicks = NiceAxisTickGrid.Generate(Viewport.YMin, Viewport.YMax, bestYTickRequest, out var yStep);
            var lastLabelTop = float.PositiveInfinity;
            foreach (var tick in yTicks)
            {
                if (!NonNegativeAxisTickPolicy.AllowsLabel(tick))
                {
                    continue;
                }

                var y = ToPixelY(tick, plotRect);
                canvas.DrawLine(plotRect.Left - yTickLength, y, plotRect.Left, y, AxisStrokePaint);
                var baseline = y - (axisMetrics.Ascent + axisMetrics.Descent) * 0.5f;
                var top = baseline + axisMetrics.Ascent;
                var bottom = baseline + axisMetrics.Descent;
                if (bottom >= lastLabelTop - yMinGap)
                {
                    continue;
                }

                var label = AxisTickLabelFormatter.FormatSnappedToGrid(tick, yStep, AxisLabelPrecisionY, null, AxisLabelFormatY);
                canvas.DrawText(label, yTickRight, baseline, SKTextAlign.Right, AxisFont, AxisLabelPaint);
                lastLabelTop = top;
            }
        }

        if (ShowXAxis && !string.IsNullOrWhiteSpace(AxisLabelX))
        {
            var cellBottom = plotRect.Bottom + xAxisReserveY;
            var titleBottom = cellBottom - (2f * zs) + (axisFontHeight * 0.3f);
            var xTitleBaseline = titleBottom - axisMetrics.Descent;
            canvas.DrawText(AxisLabelX, (plotRect.Left + plotRect.Right) / 2f, xTitleBaseline, SKTextAlign.Center, AxisFont, AxisLabelPaint);
        }

        if (ShowYAxis && !string.IsNullOrWhiteSpace(AxisLabelY))
        {
            var yCaption = AxisLabelY;
            var yTitleLeftInset = (ChartPlotLayout.CellPadding * zs) + (2f * zs);
            var yTitleCenterX = yTitleLeftInset + Math.Max(0f, -axisMetrics.Ascent) - (axisFontHeight * 0.5f);
            canvas.Save();
            canvas.Translate(yTitleCenterX, (plotRect.Top + plotRect.Bottom) / 2f);
            canvas.RotateDegrees(-90f);
            canvas.DrawText(yCaption, 0f, 0f, SKTextAlign.Center, AxisFont, AxisLabelPaint);
            canvas.Restore();
        }
    }

    protected override void DrawOverlay(SKCanvas canvas, SKRect plotRect)
    {
        DrawOriginGuideLines(canvas, plotRect);

        var pr = 5f * PlotUiScale;
        foreach (var point in CalibrationPoints)
        {
            var px = ToPixelX(point.X, plotRect);
            var py = ToPixelY(point.Y, plotRect);
            canvas.DrawCircle(px, py, pr, point.IsEnabled ? _pointEnabledPaint : _pointDisabledPaint);
            canvas.DrawCircle(px, py, pr, _pointStrokePaint);
        }
    }

    /// <summary>
    /// Full-height line at x=0 and full-width line at y=0 across the plot when visible.
    /// </summary>
    private void DrawOriginGuideLines(SKCanvas canvas, SKRect plotRect)
    {
        if (CalibrationPoints.Count == 0)
        {
            return;
        }

        const double o = 0d;
        if (Viewport is { XMin: <= o, XMax: >= o })
        {
            var px = ToPixelX(o, plotRect);
            var pyBottom = ToPixelY(Viewport.YMin, plotRect);
            var pyTop = ToPixelY(Viewport.YMax, plotRect);
            canvas.DrawLine(px, pyBottom, px, pyTop, _originGuidePaint);
        }

        if (Viewport is { YMin: <= o, YMax: >= o })
        {
            var pxLeft = ToPixelX(Viewport.XMin, plotRect);
            var pxRight = ToPixelX(Viewport.XMax, plotRect);
            var py = ToPixelY(o, plotRect);
            canvas.DrawLine(pxLeft, py, pxRight, py, _originGuidePaint);
        }
    }

    private static void OnCalibrationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (CalibrationCurveControl)d;
        c.SyncChartPointsFromCalibration();
        c.FitViewportToCalibrationPoints();
        c.RequestRender();
    }

    private static void OnFitModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (CalibrationCurveControl)d;
        c.FitViewportToCalibrationPoints();
        c.RequestRender();
    }

    private void SyncChartPointsFromCalibration()
    {
        var pts = CalibrationPoints;
        if (pts.Count == 0)
        {
            Points = Array.Empty<ChartPoint>();
            return;
        }

        Points = pts.Select(p => new ChartPoint(p.X, p.Y)).ToArray();
    }

    private void FitViewportToCalibrationPoints()
    {
        if (!CalibrationViewportBounds.TryGetZoomOutExtents(CalibrationPoints, FitMode, FitSamplingSegments, out var xMin, out var xMax, out var yMin, out var yMax))
        {
            Viewport.XMin = 0;
            Viewport.XMax = 1;
            Viewport.YMin = 0;
            Viewport.YMax = 1;
            Viewport.EnsureMinimumSize();
            return;
        }

        Viewport.XMin = xMin;
        Viewport.YMin = yMin;
        Viewport.XMax = xMax;
        Viewport.YMax = yMax;
        Viewport.EnsureMinimumSize();
    }

    private CalibrationPoint? FindNearest(ChartPoint clicked, double normalizedDistanceThreshold)
    {
        CalibrationPoint? nearest = null;
        var best = double.MaxValue;
        var xRange = Math.Max(1e-9, Viewport.XMax - Viewport.XMin);
        var yRange = Math.Max(1e-9, Viewport.YMax - Viewport.YMin);
        foreach (var point in CalibrationPoints)
        {
            var dx = (point.X - clicked.X) / xRange;
            var dy = (point.Y - clicked.Y) / yRange;
            var distance = dx * dx + dy * dy;
            if (distance < best)
            {
                best = distance;
                nearest = point;
            }
        }

        return best <= normalizedDistanceThreshold * normalizedDistanceThreshold ? nearest : null;
    }

}
