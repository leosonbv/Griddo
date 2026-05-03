namespace Plotto.Charting.Core;

/// <summary>
/// Outer zoom/pan limits for calibration charts (SRP: margin box from points + fitted curve samples).
/// </summary>
public static class CalibrationViewportBounds
{
    /// <summary>
    /// Zoom-out box: 5% margin below/left of origin on X and Y; high side = max of points and fitted curve + 5%.
    /// </summary>
    public static bool TryGetZoomOutExtents(
        IReadOnlyList<CalibrationPoint> pts,
        CalibrationFitMode fitMode,
        int fitSamplingSegments,
        out double xMinLim,
        out double xMaxLim,
        out double yMinLim,
        out double yMaxLim)
    {
        xMinLim = 0d;
        xMaxLim = 1d;
        yMinLim = 0d;
        yMaxLim = 1d;
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
        if (CalibrationFitSolver.TryCreateEvaluator(fitMode, enabled, out var eval))
        {
            interpolatedMaxX = Math.Max(interpolatedMaxX, xmax);
            var sampleXMax = Math.Max(0d, interpolatedMaxX);
            fitSamplingSegments = Math.Max(1, fitSamplingSegments);
            for (var i = 0; i <= fitSamplingSegments; i++)
            {
                var x = sampleXMax * (i / (double)fitSamplingSegments);
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
}
