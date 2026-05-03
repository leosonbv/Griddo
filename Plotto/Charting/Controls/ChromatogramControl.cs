using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SkiaSharp;
using Plotto.Charting.Core;

namespace Plotto.Charting.Controls;

public class ChromatogramControl : SkiaChartBaseControl
{
    private const double ActivationMoveToleranceDip = 4d;
    private Point _activationPressPosition;
    private bool _awaitingActivationClick;

    private IntegrationRegion? _activeRegion;
    private bool _isIntegrationDragActive;
    private readonly List<double> _peakSplitStaticX = [];
    private double? _peakSplitHoverX;
    private readonly SKPaint _integrationFillPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = new SKColor(200, 255, 170, 110)
    };

    private readonly SKPaint _integrationLinePaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1.5f,
        Color = SKColors.Red
    };

    public event EventHandler<IntegrationRegionEventArgs>? IntegrationChanged;

    public ChromatogramControl()
    {
        var descriptor = DependencyPropertyDescriptor.FromProperty(
            SkiaChartBaseControl.RenderModeProperty,
            typeof(ChromatogramControl));
        descriptor?.AddValueChanged(this, (_, _) => OnRenderModeChanged());
    }

    protected override void ApplyUiScaleToResources()
    {
        base.ApplyUiScaleToResources();
        _integrationLinePaint.StrokeWidth = Math.Max(0.5f, 1.5f * PlotUiScale);
    }

    /// <summary>
    /// Caps vertical zoom-out: visible Y span cannot exceed <c>ymaxPlot / 0.5</c> (twice the max Y in the trace).
    /// Does not run on Y zoom-in or on pan.
    /// </summary>
    protected override void OnAfterYZoom(double scale)
    {
        if (scale <= 1.0 + 1e-15)
        {
            return;
        }

        var points = Points;
        if (points.Count == 0)
        {
            return;
        }

        GetPlotPointMinMax(points, out _, out _, out _, out var ymaxPlot);
        const double eps = 1e-12;
        if (ymaxPlot <= eps)
        {
            return;
        }

        var maxSpan = ymaxPlot / 0.5;
        var h = Viewport.YMax - Viewport.YMin;
        if (h <= maxSpan + eps)
        {
            return;
        }

        var mid = (Viewport.YMin + Viewport.YMax) * 0.5;
        Viewport.YMin = mid - maxSpan * 0.5;
        Viewport.YMax = mid + maxSpan * 0.5;
        Viewport.EnsureMinimumSize();
        ApplyViewportInteractionClamp();
    }

    public IReadOnlyList<IntegrationRegion> IntegrationRegions
    {
        get => (IReadOnlyList<IntegrationRegion>)GetValue(IntegrationRegionsProperty);
        set => SetValue(IntegrationRegionsProperty, value);
    }

    public static readonly DependencyProperty IntegrationRegionsProperty =
        DependencyProperty.Register(
            nameof(IntegrationRegions),
            typeof(IReadOnlyList<IntegrationRegion>),
            typeof(ChromatogramControl),
            new FrameworkPropertyMetadata(Array.Empty<IntegrationRegion>(), FrameworkPropertyMetadataOptions.AffectsRender));

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (RequireActivationClick && RenderMode == ChartRenderMode.Renderer && e.ChangedButton == MouseButton.Left)
        {
            if (DeferRendererActivationToParent)
            {
                base.OnMouseDown(e);
                return;
            }

            Focus();
            _activationPressPosition = e.GetPosition(this);
            _awaitingActivationClick = true;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (RenderMode == ChartRenderMode.Editor
            && e.ChangedButton == MouseButton.Left
            && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            Focus();
            var x = ToChartPoint(e.GetPosition(this)).X;
            _peakSplitStaticX.Add(x);
            RequestRender();
            e.Handled = true;
            return;
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_awaitingActivationClick && RequireActivationClick && RenderMode == ChartRenderMode.Renderer)
        {
            var p = e.GetPosition(this);
            if (DistanceSquaredDip(p, _activationPressPosition) > ActivationMoveToleranceDip * ActivationMoveToleranceDip)
            {
                _awaitingActivationClick = false;
                ReleaseMouseCapture();
            }
        }

        if (RenderMode == ChartRenderMode.Editor
            && (Keyboard.Modifiers & ModifierKeys.Control) != 0
            && e.LeftButton == MouseButtonState.Released)
        {
            _peakSplitHoverX = ToChartPoint(e.GetPosition(this)).X;
            InvalidateVisual();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_awaitingActivationClick && RequireActivationClick && RenderMode == ChartRenderMode.Renderer && e.ChangedButton == MouseButton.Left)
        {
            _awaitingActivationClick = false;
            ReleaseMouseCapture();
            var up = e.GetPosition(this);
            if (DistanceSquaredDip(up, _activationPressPosition) <= ActivationMoveToleranceDip * ActivationMoveToleranceDip)
            {
                RenderMode = ChartRenderMode.Editor;
                InvalidateVisual();
                e.Handled = true;
                return;
            }
        }

        base.OnMouseUp(e);
        if (e.ChangedButton == MouseButton.Left)
        {
            _isIntegrationDragActive = false;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && RequireActivationClick && RenderMode == ChartRenderMode.Editor)
        {
            RenderMode = ChartRenderMode.Renderer;
            _awaitingActivationClick = false;
            _peakSplitStaticX.Clear();
            _peakSplitHoverX = null;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);

        if (RenderMode == ChartRenderMode.Editor && e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            if (Mouse.LeftButton == MouseButtonState.Released && IsMouseOver)
            {
                _peakSplitHoverX = ToChartPoint(Mouse.GetPosition(this)).X;
            }

            InvalidateVisual();
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (RenderMode == ChartRenderMode.Editor && e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            _peakSplitHoverX = null;
            InvalidateVisual();
        }
    }

    private void OnRenderModeChanged()
    {
        _awaitingActivationClick = false;
        _isIntegrationDragActive = false;
        _peakSplitStaticX.Clear();
        _peakSplitHoverX = null;

        if (RenderMode == ChartRenderMode.Editor)
        {
            IntegrationRegions = Array.Empty<IntegrationRegion>();
            _activeRegion = null;
        }
        else if (RenderMode == ChartRenderMode.Renderer)
        {
            IntegrationRegions = Array.Empty<IntegrationRegion>();
            _activeRegion = null;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Clears manual integration geometry and peak-split overlays without changing <see cref="SkiaChartBaseControl.RenderMode"/>.
    /// Used when the chart is rebound to another dataset (e.g. shared editor moved to another cell).
    /// </summary>
    public void ResetIntegrationDisplay()
    {
        _isIntegrationDragActive = false;
        _peakSplitStaticX.Clear();
        _peakSplitHoverX = null;
        IntegrationRegions = Array.Empty<IntegrationRegion>();
        _activeRegion = null;
        InvalidateVisual();
    }

    private static double DistanceSquaredDip(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    protected override void OnChartMouseDown(ChartPoint point, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            return;
        }

        _peakSplitStaticX.Clear();
        _peakSplitHoverX = null;
        _isIntegrationDragActive = true;
        IntegrationRegions = Array.Empty<IntegrationRegion>();
        var p = ClampBaselineAnchor(point);
        _activeRegion = new IntegrationRegion(p, p);
        RequestRender();
    }

    protected override void OnChartMouseDrag(ChartPoint point, MouseEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            return;
        }

        if (!_isIntegrationDragActive || _activeRegion is null)
        {
            return;
        }

        var start = _activeRegion.Value.Start;
        _activeRegion = new IntegrationRegion(start, ClampBaselineAnchor(point));
        RequestRender();
    }

    protected override void OnChartMouseUp(ChartPoint point, MouseButtonEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            return;
        }

        if (!_isIntegrationDragActive || _activeRegion is null || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        // Mouse-up only ends the drag. It must not modify line geometry.
        var committed = _activeRegion.Value;
        IntegrationRegions = new List<IntegrationRegion> { committed };
        _activeRegion = null;
        _isIntegrationDragActive = false;
        IntegrationChanged?.Invoke(this, new IntegrationRegionEventArgs(committed));
        RequestRender();
    }

    protected override void DrawOverlay(SKCanvas canvas, SKRect plotRect)
    {
        foreach (var region in IntegrationRegions)
        {
            DrawIntegrationFill(canvas, plotRect, region);
        }

        if (_activeRegion is { } activeFill)
        {
            DrawIntegrationFill(canvas, plotRect, activeFill);
        }

        foreach (var region in IntegrationRegions)
        {
            DrawIntegrationLine(canvas, plotRect, region);
        }

        if (_activeRegion is { } activeLine)
        {
            DrawIntegrationLine(canvas, plotRect, activeLine);
        }

        if (RenderMode == ChartRenderMode.Editor && TryGetPrimaryIntegrationRegion(out var splitRef))
        {
            foreach (var xSplit in _peakSplitStaticX)
            {
                DrawPeakSplitVertical(canvas, plotRect, xSplit, splitRef);
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0
                && Mouse.LeftButton == MouseButtonState.Released
                && _peakSplitHoverX is { } hx)
            {
                DrawPeakSplitVertical(canvas, plotRect, hx, splitRef);
            }
        }
    }

    private bool TryGetPrimaryIntegrationRegion(out IntegrationRegion region)
    {
        if (_activeRegion is { } a)
        {
            region = a;
            return true;
        }

        if (IntegrationRegions.Count > 0)
        {
            region = IntegrationRegions[0];
            return true;
        }

        region = default;
        return false;
    }

    /// <summary>
    /// Vertical from sloped integration baseline to signal at <paramref name="xData"/>, same stroke as manual integration.
    /// </summary>
    private void DrawPeakSplitVertical(SKCanvas canvas, SKRect plotRect, double xData, IntegrationRegion region)
    {
        var pts = Points;
        if (pts.Count == 0)
        {
            return;
        }

        var ordered = pts.OrderBy(p => p.X).ToList();
        var xS = region.Start.X;
        var xE = region.End.X;
        var dx = xE - xS;
        if (Math.Abs(dx) < 1e-15)
        {
            return;
        }

        var ySigS = InterpolateYOnCurve(ordered, xS);
        var ySigE = InterpolateYOnCurve(ordered, xE);
        var yBaseS = Math.Min(region.Start.Y, ySigS);
        var yBaseE = Math.Min(region.End.Y, ySigE);
        var t = (xData - xS) / dx;
        var yBaseAtX = yBaseS + t * (yBaseE - yBaseS);
        var ySig = InterpolateYOnCurve(ordered, xData);

        var px = ToPixelX(xData, plotRect);
        var pyBase = ToPixelY(yBaseAtX, plotRect);
        var pySig = ToPixelY(ySig, plotRect);
        canvas.DrawLine(px, pyBase, px, pySig, _integrationLinePaint);
    }

    private void DrawIntegrationFill(SKCanvas canvas, SKRect plotRect, IntegrationRegion region)
    {
        var pts = Points;
        if (pts.Count == 0)
        {
            return;
        }

        var ordered = pts.OrderBy(p => p.X).ToList();
        var xMin = Math.Min(region.Start.X, region.End.X);
        var xMax = Math.Max(region.Start.X, region.End.X);
        if (xMax - xMin < 1e-12)
        {
            return;
        }

        var yLeft = InterpolateYOnCurve(ordered, xMin);
        var yRight = InterpolateYOnCurve(ordered, xMax);

        var builder = new SKPathBuilder();
        builder.MoveTo(ToPixelX(xMin, plotRect), ToPixelY(yLeft, plotRect));

        foreach (var p in ordered)
        {
            if (p.X > xMin && p.X < xMax)
            {
                builder.LineTo(ToPixelX(p.X, plotRect), ToPixelY(p.Y, plotRect));
            }
        }

        builder.LineTo(ToPixelX(xMax, plotRect), ToPixelY(yRight, plotRect));
        var yEndBase = Math.Min(region.End.Y, InterpolateYOnCurve(ordered, region.End.X));
        var yStartBase = Math.Min(region.Start.Y, InterpolateYOnCurve(ordered, region.Start.X));
        var yBaseAtXMin = region.Start.X <= region.End.X ? yStartBase : yEndBase;
        var yBaseAtXMax = region.Start.X <= region.End.X ? yEndBase : yStartBase;
        builder.LineTo(ToPixelX(xMax, plotRect), ToPixelY(yBaseAtXMax, plotRect));
        builder.LineTo(ToPixelX(xMin, plotRect), ToPixelY(yBaseAtXMin, plotRect));
        builder.Close();

        using var path = builder.Detach();
        canvas.DrawPath(path, _integrationFillPaint);
    }

    private static double InterpolateYOnCurve(IReadOnlyList<ChartPoint> points, double x)
    {
        if (points.Count == 0)
        {
            return 0;
        }

        if (x <= points[0].X)
        {
            return points[0].Y;
        }

        if (x >= points[^1].X)
        {
            return points[^1].Y;
        }

        for (var i = 0; i < points.Count - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            if (x >= a.X && x <= b.X)
            {
                var dx = b.X - a.X;
                if (Math.Abs(dx) < 1e-15)
                {
                    return a.Y;
                }

                var t = (x - a.X) / dx;
                return a.Y + (t * (b.Y - a.Y));
            }
        }

        return points[^1].Y;
    }

    private void DrawIntegrationLine(SKCanvas canvas, SKRect plotRect, IntegrationRegion region)
    {
        var pts = Points;
        if (pts.Count == 0)
        {
            var x1 = ToPixelX(region.Start.X, plotRect);
            var y1 = ToPixelY(region.Start.Y, plotRect);
            var x2 = ToPixelX(region.End.X, plotRect);
            var y2 = ToPixelY(region.End.Y, plotRect);
            canvas.DrawLine(x1, y1, x2, y2, _integrationLinePaint);
            return;
        }

        var ordered = pts.OrderBy(p => p.X).ToList();
        var xS = region.Start.X;
        var xE = region.End.X;
        var ySigS = InterpolateYOnCurve(ordered, xS);
        var ySigE = InterpolateYOnCurve(ordered, xE);
        var yBaseS = Math.Min(region.Start.Y, ySigS);
        var yBaseE = Math.Min(region.End.Y, ySigE);

        var pxS = ToPixelX(xS, plotRect);
        var pxE = ToPixelX(xE, plotRect);
        var pyBaseS = ToPixelY(yBaseS, plotRect);
        var pyBaseE = ToPixelY(yBaseE, plotRect);
        var pySigS = ToPixelY(ySigS, plotRect);
        var pySigE = ToPixelY(ySigE, plotRect);

        canvas.DrawLine(pxS, pyBaseS, pxS, pySigS, _integrationLinePaint);
        canvas.DrawLine(pxS, pyBaseS, pxE, pyBaseE, _integrationLinePaint);
        canvas.DrawLine(pxE, pyBaseE, pxE, pySigE, _integrationLinePaint);
    }

    /// <summary>
    /// Keeps baseline endpoints at or below the chromatogram (data Y ≤ signal at that X).
    /// </summary>
    private ChartPoint ClampBaselineAnchor(ChartPoint point)
    {
        var pts = Points;
        if (pts.Count == 0)
        {
            return point;
        }

        var ordered = pts.OrderBy(p => p.X).ToList();
        var yCurve = InterpolateYOnCurve(ordered, point.X);
        return new ChartPoint(point.X, Math.Min(point.Y, yCurve));
    }
}
