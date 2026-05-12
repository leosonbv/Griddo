namespace Plotto.Charting.Core;

/// <summary>
/// Outer zoom/pan limits for calibration charts from calibration points and optional fitted-curve samples.
/// </summary>
public static class CalibrationViewportBounds
{
    /// <summary>
    /// Zoom-out box: 5% of total X/Y span on the low side and 10% on the high side from calibration points, curve overlay, and extras.
    /// The viewport always includes x = 0 and y = 0 with at least 5% of the final span below each axis.
    /// </summary>
    public static bool TryGetZoomOutExtents(
        IReadOnlyList<CalibrationPoint> pts,
        out double xMinLim,
        out double xMaxLim,
        out double yMinLim,
        out double yMaxLim) =>
        TryGetZoomOutExtents(pts, null, null, out xMinLim, out xMaxLim, out yMinLim, out yMaxLim);

    /// <summary>
    /// Same as <see cref="TryGetZoomOutExtents(IReadOnlyList{CalibrationPoint}, out double, out double, out double, out double)"/> but unions extents with an optional curve overlay.
    /// </summary>
    public static bool TryGetZoomOutExtents(
        IReadOnlyList<CalibrationPoint> pts,
        IReadOnlyList<ChartPoint>? curveOverlay,
        out double xMinLim,
        out double xMaxLim,
        out double yMinLim,
        out double yMaxLim) =>
        TryGetZoomOutExtents(pts, curveOverlay, null, out xMinLim, out xMaxLim, out yMinLim, out yMaxLim);

    /// <summary>
    /// Unions calibration points, optional curve overlay, and optional extra plot points (e.g. quantifier guides).
    /// </summary>
    public static bool TryGetZoomOutExtents(
        IReadOnlyList<CalibrationPoint> pts,
        IReadOnlyList<ChartPoint>? curveOverlay,
        IReadOnlyList<ChartPoint>? additionalExtents,
        out double xMinLim,
        out double xMaxLim,
        out double yMinLim,
        out double yMaxLim)
    {
        xMinLim = 0d;
        xMaxLim = 1d;
        yMinLim = 0d;
        yMaxLim = 1d;
        if (pts.Count == 0
            && (curveOverlay == null || curveOverlay.Count == 0)
            && (additionalExtents == null || additionalExtents.Count == 0))
        {
            return false;
        }

        var xmin = double.PositiveInfinity;
        var xmax = double.NegativeInfinity;
        var ymin = double.PositiveInfinity;
        var ymax = double.NegativeInfinity;
        var hasExtents = false;
        foreach (var p in pts)
        {
            hasExtents = true;
            if (p.X < xmin)
            {
                xmin = p.X;
            }

            if (p.X > xmax)
            {
                xmax = p.X;
            }

            if (p.Y < ymin)
            {
                ymin = p.Y;
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
                hasExtents = true;
                if (p.X < xmin)
                {
                    xmin = p.X;
                }

                if (p.X > xmax)
                {
                    xmax = p.X;
                }

                if (p.Y < ymin)
                {
                    ymin = p.Y;
                }

                if (p.Y > ymax)
                {
                    ymax = p.Y;
                }
            }
        }

        if (additionalExtents != null)
        {
            foreach (var p in additionalExtents)
            {
                hasExtents = true;
                if (p.X < xmin)
                {
                    xmin = p.X;
                }

                if (p.X > xmax)
                {
                    xmax = p.X;
                }

                if (p.Y < ymin)
                {
                    ymin = p.Y;
                }

                if (p.Y > ymax)
                {
                    ymax = p.Y;
                }
            }
        }

        if (!hasExtents)
        {
            return false;
        }

        var spanX = Math.Max(1e-6, xmax - xmin);
        var spanY = Math.Max(1e-6, ymax - ymin);
        const double lowSidePadFraction = 0.05;
        const double highSidePadFraction = 0.10;
        xMinLim = xmin - spanX * lowSidePadFraction;
        yMinLim = ymin - spanY * lowSidePadFraction;
        xMaxLim = xmax + spanX * highSidePadFraction;
        yMaxLim = ymax + spanY * highSidePadFraction;

        var viewportSpanX = xMaxLim - xMinLim;
        var viewportSpanY = yMaxLim - yMinLim;
        xMinLim = Math.Min(xMinLim, -viewportSpanX * lowSidePadFraction);
        yMinLim = Math.Min(yMinLim, -viewportSpanY * lowSidePadFraction);
        return true;
    }
}
