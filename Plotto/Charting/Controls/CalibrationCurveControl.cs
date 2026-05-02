using System.Linq;
using System.Windows;
using System.Windows.Input;
using SkiaSharp;
using Plotto.Charting.Core;

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
            !TryGetCalibrationZoomOutExtents(out var xMinLim, out var xMaxLim, out var yMinLim, out var yMaxLim))
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
        if (TryGetPolynomial(enabled, out var eval))
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
        if (!TryGetPolynomial(enabled, out var eval))
        {
            return;
        }

        var x0 = Viewport.XMin;
        var x1 = Viewport.XMax;
        const int segments = 160;
        using var builder = new SKPathBuilder();
        var first = true;
        for (var i = 0; i <= segments; i++)
        {
            var t = i / (double)segments;
            var xd = x0 + t * (x1 - x0);
            var yd = eval(xd);
            var px = ToPixelX(xd, plotRect);
            var py = ToPixelY(yd, plotRect);
            if (first)
            {
                builder.MoveTo(px, py);
                first = false;
            }
            else
            {
                builder.LineTo(px, py);
            }
        }

        using var path = builder.Detach();
        canvas.DrawPath(path, _fitPaint);
    }

    protected override void DrawAxes(SKCanvas canvas, SKRect plotRect)
    {
        if (UseSparklineLayout)
        {
            return;
        }

        const double z = 0d;
        const double eps = 1e-12;
        // x=0 lies left of the plot: show y-axis spine. y=0 lies below the plot bottom: show x-axis spine.
        var showYAxisLine = Viewport.XMin > eps;
        var showXAxisLine = Viewport.YMin > eps;
        if (showYAxisLine)
        {
            canvas.DrawLine(plotRect.Left, plotRect.Top, plotRect.Left, plotRect.Bottom, AxisStrokePaint);
        }

        if (showXAxisLine)
        {
            canvas.DrawLine(plotRect.Left, plotRect.Bottom, plotRect.Right, plotRect.Bottom, AxisStrokePaint);
        }

        var x0InView = Viewport.XMin <= z && z <= Viewport.XMax;
        var y0InView = Viewport.YMin <= z && z <= Viewport.YMax;

        var zs = PlotUiScale;
        var axOff = 4f * zs;
        var below = 14f * zs;
        var topLab = 10f * zs;

        // Plot edges are always XMin/XMax and YMin/YMax — not 0 when the origin sits outside the viewport.
        if (ShouldDrawAxisLabel(Viewport.XMin))
        {
            canvas.DrawText($"{Viewport.XMin:0.##}", plotRect.Left, plotRect.Bottom + below, SKTextAlign.Left, AxisFont, AxisLabelPaint);
        }

        if (ShouldDrawAxisLabel(Viewport.XMax))
        {
            canvas.DrawText($"{Viewport.XMax:0.##}", plotRect.Right, plotRect.Bottom + below, SKTextAlign.Right, AxisFont, AxisLabelPaint);
        }

        if (ShouldDrawAxisLabel(Viewport.YMax))
        {
            canvas.DrawText($"{Viewport.YMax:0.##}", plotRect.Left - axOff, plotRect.Top + topLab, SKTextAlign.Right, AxisFont, AxisLabelPaint);
        }

        if (ShouldDrawAxisLabel(Viewport.YMin))
        {
            canvas.DrawText($"{Viewport.YMin:0.##}", plotRect.Left - axOff, plotRect.Bottom, SKTextAlign.Right, AxisFont, AxisLabelPaint);
        }

        if (x0InView && Viewport.XMin + eps < z && z < Viewport.XMax - eps)
        {
            canvas.DrawText($"{z:0.##}", ToPixelX(z, plotRect), plotRect.Bottom + below, SKTextAlign.Center, AxisFont, AxisLabelPaint);
        }

        if (y0InView && Viewport.YMin + eps < z && z < Viewport.YMax - eps)
        {
            canvas.DrawText($"{z:0.##}", plotRect.Left - axOff, ToPixelY(z, plotRect), SKTextAlign.Right, AxisFont, AxisLabelPaint);
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
        if (Viewport.XMin <= o && o <= Viewport.XMax)
        {
            var px = ToPixelX(o, plotRect);
            var pyBottom = ToPixelY(Viewport.YMin, plotRect);
            var pyTop = ToPixelY(Viewport.YMax, plotRect);
            canvas.DrawLine(px, pyBottom, px, pyTop, _originGuidePaint);
        }

        if (Viewport.YMin <= o && o <= Viewport.YMax)
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
        if (!TryGetCalibrationZoomOutExtents(out var xMin, out var xMax, out var yMin, out var yMax))
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

    /// <summary>
    /// Zoom-out box: 5% margin below/left of origin on X and Y; high side = max of points and fitted curve + 5%.
    /// </summary>
    private bool TryGetCalibrationZoomOutExtents(
        out double xMinLim,
        out double xMaxLim,
        out double yMinLim,
        out double yMaxLim)
    {
        xMinLim = 0d;
        xMaxLim = 1d;
        yMinLim = 0d;
        yMaxLim = 1d;
        var pts = CalibrationPoints;
        if (pts.Count == 0)
        {
            return false;
        }

        var xmax = 0d;
        var ymax = 0d;
        foreach (var p in pts)
        {
            if (p.X > xmax) xmax = p.X;
            if (p.Y > ymax) ymax = p.Y;
        }

        var enabled = pts.Where(p => p.IsEnabled).ToArray();
        var interpolatedMaxX = xmax;
        var interpolatedMaxY = ymax;
        if (TryGetPolynomial(enabled, out var eval))
        {
            interpolatedMaxX = Math.Max(interpolatedMaxX, xmax);
            var sampleXMax = Math.Max(0d, interpolatedMaxX);
            for (var i = 0; i <= FitSamplingSegments; i++)
            {
                var x = sampleXMax * (i / (double)FitSamplingSegments);
                var y = eval(x);
                if (double.IsFinite(y))
                {
                    interpolatedMaxY = Math.Max(interpolatedMaxY, y);
                }
            }
        }

        var maxX = Math.Max(xmax, interpolatedMaxX);
        var maxY = Math.Max(ymax, interpolatedMaxY);
        var padX = Math.Max(1e-6, maxX * 0.05);
        var padY = Math.Max(1e-6, maxY * 0.05);
        xMinLim = -padX;
        yMinLim = -padY;
        xMaxLim = maxX + padX;
        yMaxLim = maxY + padY;
        return true;
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

    private bool TryGetPolynomial(CalibrationPoint[] pts, out Func<double, double> eval)
    {
        eval = _ => double.NaN;
        switch (FitMode)
        {
            case CalibrationFitMode.Linear:
                return TryFitLinear(pts, out eval);
            case CalibrationFitMode.LinearThroughOrigin:
                return TryFitLinearThroughOrigin(pts, out eval);
            case CalibrationFitMode.Quadratic:
                return TryFitQuadraticFull(pts, out eval);
            case CalibrationFitMode.QuadraticThroughOrigin:
                return TryFitQuadraticThroughOrigin(pts, out eval);
            default:
                return false;
        }
    }

    private static bool TryFitLinear(CalibrationPoint[] p, out Func<double, double> eval)
    {
        eval = _ => double.NaN;
        if (p.Length < 2)
        {
            return false;
        }

        double sumX = 0, sumY = 0, sumXX = 0, sumXY = 0;
        foreach (var pt in p)
        {
            sumX += pt.X;
            sumY += pt.Y;
            sumXX += pt.X * pt.X;
            sumXY += pt.X * pt.Y;
        }

        var n = p.Length;
        var denom = n * sumXX - sumX * sumX;
        if (Math.Abs(denom) < 1e-18)
        {
            return false;
        }

        var slope = (n * sumXY - sumX * sumY) / denom;
        var intercept = (sumY - slope * sumX) / n;
        eval = x => intercept + slope * x;
        return true;
    }

    private static bool TryFitLinearThroughOrigin(CalibrationPoint[] p, out Func<double, double> eval)
    {
        eval = _ => double.NaN;
        if (p.Length < 1)
        {
            return false;
        }

        double sumXX = 0, sumXY = 0;
        foreach (var pt in p)
        {
            sumXX += pt.X * pt.X;
            sumXY += pt.X * pt.Y;
        }

        if (Math.Abs(sumXX) < 1e-18)
        {
            return false;
        }

        var slope = sumXY / sumXX;
        eval = x => slope * x;
        return true;
    }

    private static bool TryFitQuadraticFull(CalibrationPoint[] p, out Func<double, double> eval)
    {
        eval = _ => double.NaN;
        if (p.Length < 3)
        {
            return false;
        }

        double s0 = p.Length, sx = 0, sx2 = 0, sx3 = 0, sx4 = 0, sy = 0, sxy = 0, sx2y = 0;
        foreach (var pt in p)
        {
            var x = pt.X;
            var y = pt.Y;
            var x2 = x * x;
            sx += x;
            sx2 += x2;
            sx3 += x * x2;
            sx4 += x2 * x2;
            sy += y;
            sxy += x * y;
            sx2y += x2 * y;
        }

        // [a b c] for y = a + b*x + c*x^2
        Span<double> m = stackalloc double[9];
        m[0] = s0; m[1] = sx; m[2] = sx2;
        m[3] = sx; m[4] = sx2; m[5] = sx3;
        m[6] = sx2; m[7] = sx3; m[8] = sx4;
        Span<double> rhs = stackalloc double[3];
        rhs[0] = sy;
        rhs[1] = sxy;
        rhs[2] = sx2y;

        if (!Solve3x3(m, rhs, out var a, out var b, out var c))
        {
            return false;
        }

        eval = x => a + b * x + c * x * x;
        return true;
    }

    private static bool TryFitQuadraticThroughOrigin(CalibrationPoint[] p, out Func<double, double> eval)
    {
        eval = _ => double.NaN;
        if (p.Length < 2)
        {
            return false;
        }

        double sx2 = 0, sx3 = 0, sx4 = 0, sxy = 0, sx2y = 0;
        foreach (var pt in p)
        {
            var x = pt.X;
            var y = pt.Y;
            var x2 = x * x;
            sx2 += x2;
            sx3 += x * x2;
            sx4 += x2 * x2;
            sxy += x * y;
            sx2y += x2 * y;
        }

        var det = sx2 * sx4 - sx3 * sx3;
        if (Math.Abs(det) < 1e-18)
        {
            return false;
        }

        var b = (sxy * sx4 - sx2y * sx3) / det;
        var c = (sx2y * sx2 - sxy * sx3) / det;
        eval = x => b * x + c * x * x;
        return true;
    }

    private static bool Solve3x3(Span<double> m, Span<double> rhs, out double a, out double b, out double c)
    {
        a = b = c = 0;
        var work = new double[12];
        for (var i = 0; i < 3; i++)
        {
            for (var j = 0; j < 3; j++)
            {
                work[i * 4 + j] = m[i * 3 + j];
            }

            work[i * 4 + 3] = rhs[i];
        }

        for (var col = 0; col < 3; col++)
        {
            var pivotRow = col;
            var best = Math.Abs(work[col * 4 + col]);
            for (var r = col + 1; r < 3; r++)
            {
                var v = Math.Abs(work[r * 4 + col]);
                if (v > best)
                {
                    best = v;
                    pivotRow = r;
                }
            }

            if (best < 1e-18)
            {
                return false;
            }

            if (pivotRow != col)
            {
                for (var j = 0; j < 4; j++)
                {
                    (work[col * 4 + j], work[pivotRow * 4 + j]) = (work[pivotRow * 4 + j], work[col * 4 + j]);
                }
            }

            var div = work[col * 4 + col];
            for (var j = 0; j < 4; j++)
            {
                work[col * 4 + j] /= div;
            }

            for (var r = 0; r < 3; r++)
            {
                if (r == col)
                {
                    continue;
                }

                var f = work[r * 4 + col];
                if (Math.Abs(f) < 1e-18)
                {
                    continue;
                }

                for (var j = 0; j < 4; j++)
                {
                    work[r * 4 + j] -= f * work[col * 4 + j];
                }
            }
        }

        a = work[3];
        b = work[7];
        c = work[11];
        return true;
    }
}
