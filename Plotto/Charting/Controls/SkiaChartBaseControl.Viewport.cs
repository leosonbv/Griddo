using System.Windows;
using Plotto.Charting.Core;

namespace Plotto.Charting.Controls;

public abstract partial class SkiaChartBaseControl
{
    private void UpdateViewportFromData()
    {
        var points = Points;
        if (points.Count == 0)
        {
            _viewportWheelClamp.SetEmptyDataDefaults(Viewport);
            return;
        }

        _viewportWheelClamp.FitViewportToSeriesMargins(Viewport, points);
        ApplyViewportInteractionClamp();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>After wheel zoom, pan, or drag-zoom; default uses point-based X/Y clamps.</summary>
    protected virtual void ApplyViewportInteractionClamp()
    {
        _viewportWheelClamp.ClampViewportToWheelZoomLimits(
            Viewport,
            Points,
            clampYToDataFloor: !IsViewportClampAfterRectZoom);
    }

    protected void ZoomXAt(Point pivot, double scale)
    {
        var pivotData = ToChartPoint(pivot);
        var xRange = (Viewport.XMax - Viewport.XMin) * scale;
        Viewport.XMin = pivotData.X - (pivotData.X - Viewport.XMin) * scale;
        Viewport.XMax = Viewport.XMin + xRange;
        ApplyViewportInteractionClamp();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    protected void ZoomYAt(Point pivot, double scale)
    {
        var pivotData = ToChartPoint(pivot);
        var yRange = (Viewport.YMax - Viewport.YMin) * scale;
        Viewport.YMin = pivotData.Y - (pivotData.Y - Viewport.YMin) * scale;
        Viewport.YMax = Viewport.YMin + yRange;
        ApplyViewportInteractionClamp();
        OnAfterYZoom(scale);
        ViewportChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>
    /// Called after <see cref="ApplyViewportInteractionClamp"/> from <see cref="ZoomYAt"/> only.
    /// <paramref name="scale"/> &gt; 1 zooms out (larger Y span); &lt; 1 zooms in.
    /// </summary>
    protected virtual void OnAfterYZoom(double scale)
    {
    }

    private void ApplyRightDragZoom(Point a, Point b)
    {
        if (PlotRect.Width <= 1 || PlotRect.Height <= 1)
        {
            return;
        }

        var x0 = Math.Clamp(Math.Min(a.X, b.X), PlotRect.Left, PlotRect.Right);
        var x1 = Math.Clamp(Math.Max(a.X, b.X), PlotRect.Left, PlotRect.Right);
        var y0 = Math.Clamp(Math.Min(a.Y, b.Y), PlotRect.Top, PlotRect.Bottom);
        var y1 = Math.Clamp(Math.Max(a.Y, b.Y), PlotRect.Top, PlotRect.Bottom);
        if (x1 - x0 < 4 || y1 - y0 < 4)
        {
            return;
        }

        var bottomLeft = _coordinates.SurfacePixelToChartPoint(new Point(x0, y1));
        var topRight = _coordinates.SurfacePixelToChartPoint(new Point(x1, y0));
        Viewport.XMin = Math.Min(bottomLeft.X, topRight.X);
        Viewport.XMax = Math.Max(bottomLeft.X, topRight.X);
        Viewport.YMin = Math.Min(bottomLeft.Y, topRight.Y);
        Viewport.YMax = Math.Max(bottomLeft.Y, topRight.Y);
        _viewportClampAfterRectZoom = true;
        try
        {
            ApplyViewportInteractionClamp();
        }
        finally
        {
            _viewportClampAfterRectZoom = false;
        }

        ViewportChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void PanByPixels(double dx, double dy)
    {
        var xPerPixel = (Viewport.XMax - Viewport.XMin) / Math.Max(1d, PlotRect.Width);
        var yPerPixel = (Viewport.YMax - Viewport.YMin) / Math.Max(1d, PlotRect.Height);
        var xDelta = dx * xPerPixel;
        var yDelta = dy * yPerPixel;

        Viewport.XMin -= xDelta;
        Viewport.XMax -= xDelta;
        Viewport.YMin += yDelta;
        Viewport.YMax += yDelta;
        ApplyViewportInteractionClamp();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>
    /// Fits the viewport to all data (full zoom out). Requires edit mode — use double right-click on the chart for reset while viewing in renderer mode.
    /// </summary>
    public void ZoomOutCompletely()
    {
        if (!CanInteract())
        {
            return;
        }

        FitViewportToAllData();
    }

    private void FitViewportToAllData()
    {
        UpdateViewportFromData();
        InvalidateVisual();
    }
}
