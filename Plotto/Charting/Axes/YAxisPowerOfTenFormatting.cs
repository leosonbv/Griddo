using System.Globalization;
using SkiaSharp;
using Plotto.Charting.Geometry;

namespace Plotto.Charting.Axes;

/// <summary>
/// When the Y range is large, scale tick labels by a power of ten and show a 10ⁿ-style badge in the Y-axis band.
/// </summary>
public static class YAxisPowerOfTenFormatting
{
    private const string TenText = "10";

    /// <summary>Apply mantissa scaling when the larger magnitude of Y exceeds this value.</summary>
    public const double MagnitudeThreshold = 1000;

    public static bool TryGetScale(double yMin, double yMax, out double scale, out int exponent)
    {
        scale = 1;
        exponent = 0;
        var yAbsMax = Math.Max(Math.Abs(yMin), Math.Abs(yMax));
        if (!(yAbsMax > MagnitudeThreshold) || !double.IsFinite(yAbsMax) || yAbsMax <= 0)
        {
            return false;
        }

        exponent = (int)Math.Floor(Math.Log10(yAbsMax));
        scale = Math.Pow(10, exponent);
        return scale > 0 && double.IsFinite(scale);
    }

    /// <summary>
    /// Axis-aligned bounds of the exponent badge (for skipping overlapping tick labels / caption).
    /// </summary>
    public static SKRect GetExponentBadgeBounds(SKRect plotRect, float plotUiScale, SKFont axisFont, int exponent)
    {
        using var font = CreateBadgeFont(axisFont);
        var layout = LayoutExponentBadge(plotRect, plotUiScale, font, exponent);
        var fm = font.Metrics;
        var top = Math.Min(layout.Baseline10 + fm.Ascent, layout.BaselineExp + fm.Ascent);
        var bottom = Math.Max(layout.Baseline10 + fm.Descent, layout.BaselineExp + fm.Descent);
        return SKRect.Create(layout.BadgeLeft, top, layout.TotalWidth, Math.Max(1e-3f, bottom - top));
    }

    /// <summary>
    /// In the strip between the cell’s left padding and the Y-axis spine (<paramref name="plotRect"/>.Left):
    /// draws “10” plus exponent (same size, exponent raised by half a line height).
    /// </summary>
    public static void DrawYAxisExponentBadge(
        SKCanvas canvas,
        SKRect plotRect,
        float plotUiScale,
        SKFont axisFont,
        SKPaint axisLabelPaint,
        int exponent)
    {
        using var font = CreateBadgeFont(axisFont);
        var layout = LayoutExponentBadge(plotRect, plotUiScale, font, exponent);
        canvas.DrawText(TenText, layout.BadgeLeft, layout.Baseline10, SKTextAlign.Left, font, axisLabelPaint);
        canvas.DrawText(layout.ExpText, layout.BadgeLeft + layout.TenWidth, layout.BaselineExp, SKTextAlign.Left, font, axisLabelPaint);
    }

    private static SKFont CreateBadgeFont(SKFont axisFont)
    {
        var baseTf = axisFont.Typeface ?? SKTypeface.Default;
        var badgeEm = axisFont.Size * 2f * 0.75f;
        return new SKFont(baseTf, badgeEm);
    }

    private readonly record struct ExponentBadgeLayout(
        float BadgeLeft,
        float Baseline10,
        float BaselineExp,
        float TenWidth,
        float TotalWidth,
        string ExpText);

    private static ExponentBadgeLayout LayoutExponentBadge(SKRect plotRect, float plotUiScale, SKFont font, int exponent)
    {
        var zs = plotUiScale;
        var cellPad = ChartPlotLayout.CellPadding * zs;
        var spineGap = 4f * zs;

        var fm = font.Metrics;
        var lineHeight = fm.Descent - fm.Ascent;
        var raise = lineHeight * 0.5f;

        var padTop = 4f * zs;
        var baseline10 = plotRect.Top + padTop - fm.Ascent;

        var expText = exponent.ToString(CultureInfo.InvariantCulture);
        var w10 = font.MeasureText(TenText);
        var wExp = font.MeasureText(expText);
        var totalW = w10 + wExp;

        var badgeLeft = cellPad + (0.5f * zs);
        var maxLeftBySpine = plotRect.Left - spineGap - totalW;
        if (maxLeftBySpine < badgeLeft)
        {
            badgeLeft = Math.Max(cellPad + (0.5f * zs), maxLeftBySpine);
        }

        var baselineExp = baseline10 - raise;
        return new ExponentBadgeLayout(badgeLeft, baseline10, baselineExp, w10, totalW, expText);
    }
}
