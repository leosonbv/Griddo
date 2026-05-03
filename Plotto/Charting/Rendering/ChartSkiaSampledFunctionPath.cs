using SkiaSharp;

namespace Plotto.Charting.Rendering;

/// <summary>
/// Builds a polyline by sampling y = f(x) across the viewport X range (SRP: smooth curve preview in pixel space).
/// </summary>
public static class ChartSkiaSampledFunctionPath
{
    public static void DrawPolyline(
        SKCanvas canvas,
        SKRect plotRect,
        double xMin,
        double xMax,
        int segmentCount,
        Func<double, double> yFromX,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        SKPaint paint)
    {
        segmentCount = Math.Max(1, segmentCount);
        using var builder = new SKPathBuilder();
        var first = true;
        for (var i = 0; i <= segmentCount; i++)
        {
            var t = i / (double)segmentCount;
            var xd = xMin + t * (xMax - xMin);
            var yd = yFromX(xd);
            var px = toPixelX(xd, plotRect);
            var py = toPixelY(yd, plotRect);
            if (first)
            {
                builder.MoveTo(px, py);
                first = false;
            }
            else
            {
                builder.LineTo(px, py);
            }
        }

        using var path = builder.Detach();
        canvas.DrawPath(path, paint);
    }
}
