using Plotto.Charting.Core;
using SkiaSharp;

namespace Plotto.Charting.Rendering;

/// <summary>
/// Renders a spectrum as vertical lines from a common baseline (SRP: stick plot only).
/// </summary>
public static class ChartSkiaSpectrumSticks
{
    public const int DefaultMinStickResolution = 200;

    public static void DrawSticks(
        SKCanvas canvas,
        IReadOnlyList<ChartPoint> points,
        SKRect plotRect,
        double baselineYData,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        SKPaint stickPaint,
        int minStickResolution = DefaultMinStickResolution)
    {
        if (points.Count == 0)
        {
            return;
        }

        var baseline = toPixelY(baselineYData, plotRect);
        var count = Math.Min(points.Count, Math.Max(minStickResolution, (int)plotRect.Width));
        var step = (double)points.Count / count;
        for (var i = 0d; i < points.Count; i += step)
        {
            var point = points[(int)i];
            var x = toPixelX(point.X, plotRect);
            var y = toPixelY(point.Y, plotRect);
            canvas.DrawLine(x, baseline, x, y, stickPaint);
        }
    }
}
