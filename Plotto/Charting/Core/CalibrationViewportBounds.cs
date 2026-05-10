namespace Plotto.Charting.Core;

/// <summary>
/// Outer zoom/pan limits for calibration charts from calibration points and optional fitted-curve samples.
/// </summary>
public static class CalibrationViewportBounds
{
    /// <summary>
    /// Zoom-out box: 5% margin below/left of origin on X and Y; high side = max of points + 5%.
    /// </summary>
    public static bool TryGetZoomOutExtents(
        IReadOnlyList<CalibrationPoint> pts,
        out double xMinLim,
        out double xMaxLim,
        out double yMinLim,
        out double yMaxLim) =>
        TryGetZoomOutExtents(pts, null, out xMinLim, out xMaxLim, out yMinLim, out yMaxLim);

    /// <summary>
    /// Same as <see cref="TryGetZoomOutExtents(IReadOnlyList{CalibrationPoint}, out double, out double, out double, out double)"/> but unions extents with an optional curve overlay.
    /// </summary>
    public static bool TryGetZoomOutExtents(
        IReadOnlyList<CalibrationPoint> pts,
        IReadOnlyList<ChartPoint>? curveOverlay,
        out double xMinLim,
        out double xMaxLim,
        out double yMinLim,
        out double yMaxLim)
    {
        xMinLim = 0d;
        xMaxLim = 1d;
        yMinLim = 0d;
        yMaxLim = 1d;
        if (pts.Count == 0 && (curveOverlay == null || curveOverlay.Count == 0))
        {
            return false;
        }

        var xmax = 0d;
        var ymax = 0d;
        foreach (var p in pts)
        {
            if (p.X > xmax)
            {
                xmax = p.X;
            }

            if (p.Y > ymax)
            {
                ymax = p.Y;
            }
        }

        if (curveOverlay != null)
        {
            foreach (var p in curveOverlay)
            {
                if (p.X > xmax)
                {
                    xmax = p.X;
                }

                if (p.Y > ymax)
                {
                    ymax = p.Y;
                }
            }
        }

        var maxX = xmax;
        var maxY = ymax;
        var padX = Math.Max(1e-6, maxX * 0.05);
        var padY = Math.Max(1e-6, maxY * 0.05);
        xMinLim = -padX;
        yMinLim = -padY;
        xMaxLim = maxX + padX;
        yMaxLim = maxY + padY;
        return true;
    }
}
