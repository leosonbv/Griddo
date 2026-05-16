using Plotto.Charting.Core;
using SkiaSharp;

namespace Plotto.Charting.Rendering;

/// <summary>
/// Manual integration overlays: fill under the trace, baseline polyline, peak-split verticals (SRP: Skia only).
/// </summary>
public static class ChartSkiaManualIntegration
{
    public static void DrawRegionFill(
        SKCanvas canvas,
        IReadOnlyList<ChartPoint> pointsSortedByXAscending,
        SKRect plotRect,
        IntegrationRegion region,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        SKPaint fillPaint)
    {
        var ordered = pointsSortedByXAscending;
        if (ordered.Count == 0)
        {
            return;
        }

        var xMin = Math.Min(region.Start.X, region.End.X);
        var xMax = Math.Max(region.Start.X, region.End.X);
        if (xMax - xMin < 1e-12)
        {
            return;
        }

        var yLeft = ChartSignalInterpolation.InterpolateYAtX(ordered, xMin);
        var yRight = ChartSignalInterpolation.InterpolateYAtX(ordered, xMax);

        var builder = new SKPathBuilder();
        builder.MoveTo(toPixelX(xMin, plotRect), toPixelY(yLeft, plotRect));

        foreach (var p in ordered)
        {
            if (p.X > xMin && p.X < xMax)
            {
                builder.LineTo(toPixelX(p.X, plotRect), toPixelY(p.Y, plotRect));
            }
        }

        builder.LineTo(toPixelX(xMax, plotRect), toPixelY(yRight, plotRect));
        var yBaseAtXMin = region.Start.X <= region.End.X ? region.Start.Y : region.End.Y;
        var yBaseAtXMax = region.Start.X <= region.End.X ? region.End.Y : region.Start.Y;
        builder.LineTo(toPixelX(xMax, plotRect), toPixelY(yBaseAtXMax, plotRect));
        builder.LineTo(toPixelX(xMin, plotRect), toPixelY(yBaseAtXMin, plotRect));
        builder.Close();

        using var path = builder.Detach();
        canvas.DrawPath(path, fillPaint);
    }

    public static void DrawRegionBaseline(
        SKCanvas canvas,
        IReadOnlyList<ChartPoint> pointsSortedByXAscending,
        SKRect plotRect,
        IntegrationRegion region,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        SKPaint linePaint)
    {
        if (pointsSortedByXAscending.Count == 0)
        {
            var x1 = toPixelX(region.Start.X, plotRect);
            var y1 = toPixelY(region.Start.Y, plotRect);
            var x2 = toPixelX(region.End.X, plotRect);
            var y2 = toPixelY(region.End.Y, plotRect);
            canvas.DrawLine(x1, y1, x2, y2, linePaint);
            return;
        }

        var ordered = pointsSortedByXAscending;
        var xS = region.Start.X;
        var xE = region.End.X;
        var ySigS = ChartSignalInterpolation.InterpolateYAtX(ordered, xS);
        var ySigE = ChartSignalInterpolation.InterpolateYAtX(ordered, xE);
        var yBaseS = region.Start.Y;
        var yBaseE = region.End.Y;

        var pxS = toPixelX(xS, plotRect);
        var pxE = toPixelX(xE, plotRect);
        var pyBaseS = toPixelY(yBaseS, plotRect);
        var pyBaseE = toPixelY(yBaseE, plotRect);
        var pySigS = toPixelY(ySigS, plotRect);
        var pySigE = toPixelY(ySigE, plotRect);

        canvas.DrawLine(pxS, pyBaseS, pxS, pySigS, linePaint);
        canvas.DrawLine(pxS, pyBaseS, pxE, pyBaseE, linePaint);
        canvas.DrawLine(pxE, pyBaseE, pxE, pySigE, linePaint);
    }

    public static void DrawRegionSignalLine(
        SKCanvas canvas,
        IReadOnlyList<ChartPoint> pointsSortedByXAscending,
        SKRect plotRect,
        IntegrationRegion region,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        SKPaint linePaint)
    {
        var ordered = pointsSortedByXAscending;
        if (ordered.Count == 0)
        {
            return;
        }

        var xMin = Math.Min(region.Start.X, region.End.X);
        var xMax = Math.Max(region.Start.X, region.End.X);
        if (xMax - xMin < 1e-12)
        {
            return;
        }

        var yLeft = ChartSignalInterpolation.InterpolateYAtX(ordered, xMin);
        var yRight = ChartSignalInterpolation.InterpolateYAtX(ordered, xMax);

        var builder = new SKPathBuilder();
        builder.MoveTo(toPixelX(xMin, plotRect), toPixelY(yLeft, plotRect));
        foreach (var p in ordered)
        {
            if (p.X > xMin && p.X < xMax)
            {
                builder.LineTo(toPixelX(p.X, plotRect), toPixelY(p.Y, plotRect));
            }
        }

        builder.LineTo(toPixelX(xMax, plotRect), toPixelY(yRight, plotRect));
        using var path = builder.Detach();
        canvas.DrawPath(path, linePaint);
    }

    public static void DrawPeakSplitVertical(
        SKCanvas canvas,
        IReadOnlyList<ChartPoint> pointsSortedByXAscending,
        SKRect plotRect,
        double xData,
        IntegrationRegion region,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        SKPaint linePaint)
    {
        var ordered = pointsSortedByXAscending;
        if (ordered.Count == 0)
        {
            return;
        }

        var xS = region.Start.X;
        var xE = region.End.X;
        var dx = xE - xS;
        if (Math.Abs(dx) < 1e-15)
        {
            return;
        }

        var ySigS = ChartSignalInterpolation.InterpolateYAtX(ordered, xS);
        var ySigE = ChartSignalInterpolation.InterpolateYAtX(ordered, xE);
        var yBaseS = region.Start.Y;
        var yBaseE = region.End.Y;
        var t = (xData - xS) / dx;
        var yBaseAtX = yBaseS + t * (yBaseE - yBaseS);
        var ySig = ChartSignalInterpolation.InterpolateYAtX(ordered, xData);

        var px = toPixelX(xData, plotRect);
        var pyBase = toPixelY(yBaseAtX, plotRect);
        var pySig = toPixelY(ySig, plotRect);
        canvas.DrawLine(px, pyBase, px, pySig, linePaint);
    }
}
