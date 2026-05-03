namespace Plotto.Charting.Core;

/// <summary>
/// Axis-aligned bounds of a point series (SRP: no rendering or viewport policy).
/// </summary>
public static class ChartSeriesBounds
{
    public static void GetExtents(IReadOnlyList<ChartPoint> points, out double xmin, out double xmax, out double ymin, out double ymax)
    {
        xmin = points[0].X;
        xmax = points[0].X;
        ymin = points[0].Y;
        ymax = points[0].Y;
        for (var i = 1; i < points.Count; i++)
        {
            var p = points[i];
            if (p.X < xmin) xmin = p.X;
            if (p.X > xmax) xmax = p.X;
            if (p.Y < ymin) ymin = p.Y;
            if (p.Y > ymax) ymax = p.Y;
        }
    }
}
