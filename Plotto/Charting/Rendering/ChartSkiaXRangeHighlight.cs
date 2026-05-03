using SkiaSharp;

namespace Plotto.Charting.Rendering;

/// <summary>
/// Vertical band between two data X values (SRP: selection / range highlight in plot space).
/// </summary>
public static class ChartSkiaXRangeHighlight
{
    public static void DrawFilledBand(
        SKCanvas canvas,
        SKRect plotRect,
        double fromX,
        double toX,
        Func<double, SKRect, float> toPixelX,
        SKPaint fillPaint,
        SKPaint strokePaint)
    {
        var x1 = toPixelX(fromX, plotRect);
        var x2 = toPixelX(toX, plotRect);
        var left = Math.Min(x1, x2);
        var right = Math.Max(x1, x2);
        var rect = new SKRect(left, plotRect.Top, right, plotRect.Bottom);
        canvas.DrawRect(rect, fillPaint);
        canvas.DrawRect(rect, strokePaint);
    }
}
