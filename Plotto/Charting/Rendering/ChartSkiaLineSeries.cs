using Plotto.Charting.Core;
using SkiaSharp;

namespace Plotto.Charting.Rendering;

/// <summary>
/// Builds and draws a single XY polyline in plot pixel space (SRP: path + optional downsampling).
/// </summary>
public static class ChartSkiaLineSeries
{
    public const int DefaultDownsampleThreshold = 500;
    public const int DefaultMinSamplesWhenDownsampled = 200;

    public static void DrawPolyline(
        SKCanvas canvas,
        IReadOnlyList<ChartPoint> points,
        SKRect plotRect,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        SKPaint linePaint,
        int downsampleThreshold = DefaultDownsampleThreshold,
        int minSamplesWhenDownsampled = DefaultMinSamplesWhenDownsampled)
    {
        if (points.Count == 0)
        {
            return;
        }

        var sampled = points.Count > downsampleThreshold
            ? ChartPointDownsampler.Downsample(points, Math.Max(minSamplesWhenDownsampled, (int)plotRect.Width))
            : points;

        var builder = new SKPathBuilder();
        var first = sampled[0];
        builder.MoveTo(toPixelX(first.X, plotRect), toPixelY(first.Y, plotRect));
        for (var i = 1; i < sampled.Count; i++)
        {
            var p = sampled[i];
            builder.LineTo(toPixelX(p.X, plotRect), toPixelY(p.Y, plotRect));
        }

        using var path = builder.Detach();
        canvas.DrawPath(path, linePaint);
    }
}
