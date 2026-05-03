namespace Plotto.Charting.Core;

/// <summary>
/// Reduces point count for rendering large series (SRP: downsampling only).
/// </summary>
public static class ChartPointDownsampler
{
    public static IReadOnlyList<ChartPoint> Downsample(IReadOnlyList<ChartPoint> points, int targetCount)
    {
        if (points.Count <= targetCount)
        {
            return points;
        }

        var step = (double)points.Count / targetCount;
        var list = new List<ChartPoint>(targetCount);
        for (var i = 0d; i < points.Count && list.Count < targetCount; i += step)
        {
            list.Add(points[(int)i]);
        }

        if (list.Count == 0 || list[^1] != points[^1])
        {
            list.Add(points[^1]);
        }

        return list;
    }
}
