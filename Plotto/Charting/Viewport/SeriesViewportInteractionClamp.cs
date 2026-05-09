using Plotto.Charting.Core;

namespace Plotto.Charting.Viewport;

/// <summary>
/// Wheel / pan viewport limits derived from plot point extents (X clamp band + Y rules).
/// </summary>
public sealed class SeriesViewportInteractionClamp
{
    private double _zoomClampXMin;
    private double _zoomClampXMax;

    /// <summary>Refreshes the X-only outer zoom limits from current series (e.g. before rubber-band zoom).</summary>
    public void ResyncXClampFromPoints(IReadOnlyList<ChartPoint> points)
    {
        if (points.Count == 0)
        {
            return;
        }

        SyncZoomClampBoundsFromPoints(points);
    }

    public void SetEmptyDataDefaults(ChartViewport viewport)
    {
        viewport.XMin = 0;
        viewport.XMax = 1;
        viewport.YMin = 0;
        viewport.YMax = 1;
        _zoomClampXMin = 0;
        _zoomClampXMax = 1;
    }

    /// <summary>Initial fit: margins around data bounds; does not apply wheel clamp.</summary>
    public void FitViewportToSeriesMargins(ChartViewport viewport, IReadOnlyList<ChartPoint> points)
    {
        ChartSeriesBounds.GetExtents(points, out var xmin, out var xmax, out var ymin, out var ymax);
        var dx = xmax - xmin;
        var dy = ymax - ymin;
        SyncZoomClampBoundsForPlotExtents(xmin, xmax, dx);

        var xMargin = Math.Max(1e-6, dx * 0.02);
        var yMargin = Math.Max(1e-6, dy * 0.05);
        viewport.XMin = xmin - xMargin;
        viewport.XMax = xmax + xMargin;
        viewport.YMin = ymin - yMargin;
        viewport.YMax = ymax + yMargin;
        if (ymin >= -1e-12)
        {
            viewport.YMin = Math.Max(0d, viewport.YMin);
        }

        viewport.EnsureMinimumSize();
    }

    /// <summary>
    /// X: viewport inside plot xmin/xmax ± 5% of horizontal data span.
    /// Y: ymin/ymax are plot-point extrema; chart height = visible span (Viewport.YMax − Viewport.YMin).
    /// </summary>
    public void ClampViewportToWheelZoomLimits(
        ChartViewport viewport,
        IReadOnlyList<ChartPoint> points,
        bool clampYToDataFloor = true)
    {
        if (points.Count == 0)
        {
            return;
        }

        SyncZoomClampBoundsFromPoints(points);

        ChartSeriesBounds.GetExtents(points, out _, out _, out var yminPlot, out _);

        var limW = _zoomClampXMax - _zoomClampXMin;
        var w = viewport.XMax - viewport.XMin;
        const double eps = 1e-12;

        if (w >= limW - eps)
        {
            viewport.XMin = _zoomClampXMin;
            viewport.XMax = _zoomClampXMax;
        }
        else
        {
            var maxXMin = _zoomClampXMax - w;
            viewport.XMin = Math.Clamp(viewport.XMin, _zoomClampXMin, maxXMin);
            viewport.XMax = viewport.XMin + w;
        }

        if (clampYToDataFloor)
        {
            ClampViewportYUsingVisibleChartHeight(viewport, yminPlot);
        }

        viewport.EnsureMinimumSize();
        if (clampYToDataFloor)
        {
            ClampViewportYUsingVisibleChartHeight(viewport, yminPlot);
        }
    }

    private void SyncZoomClampBoundsFromPoints(IReadOnlyList<ChartPoint> points)
    {
        ChartSeriesBounds.GetExtents(points, out var xmin, out var xmax, out _, out _);
        var dx = xmax - xmin;
        SyncZoomClampBoundsForPlotExtents(xmin, xmax, dx);
    }

    private void SyncZoomClampBoundsForPlotExtents(double xmin, double xmax, double dx)
    {
        var padX = Math.Max(1e-12, dx * 0.05);
        _zoomClampXMin = xmin - padX;
        _zoomClampXMax = xmax + padX;
    }

    private static void ClampViewportYUsingVisibleChartHeight(ChartViewport viewport, double yminPlot)
    {
        const double eps = 1e-12;
        var h = viewport.YMax - viewport.YMin;
        if (h <= eps)
        {
            return;
        }

        var clampedYMin = yminPlot - (0.05 * h);
        if (Math.Abs(clampedYMin - viewport.YMin) > eps)
        {
            viewport.YMin = clampedYMin;
            viewport.YMax = viewport.YMin + h;
        }
    }
}
