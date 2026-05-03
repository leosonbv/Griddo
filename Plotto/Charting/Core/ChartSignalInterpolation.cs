namespace Plotto.Charting.Core;

/// <summary>
/// Linear interpolation of Y along a polyline in X (SRP: assumes points sorted by ascending X).
/// </summary>
public static class ChartSignalInterpolation
{
    public static double InterpolateYAtX(IReadOnlyList<ChartPoint> pointsSortedByXAscending, double x)
    {
        var points = pointsSortedByXAscending;
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
}
