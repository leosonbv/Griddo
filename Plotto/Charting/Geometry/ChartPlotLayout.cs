using SkiaSharp;

namespace Plotto.Charting.Geometry;

/// <summary>
/// Pure layout: maps Skia surface dimensions to the inner plot rectangle (padding and axis bands).
/// </summary>
public static class ChartPlotLayout
{
    public const float CellPadding = 4f;
    public const float AxisReserveX = 36f;
    public const float AxisReserveY = 18f;
    private const float YAxisTitleInsetMultiplier = 8.2f;

    /// <summary>Horizontal offset for Y-axis numeric labels (DIP scale).</summary>
    public static float AxisLabelInsetFromPlotLeft(float plotUiScale) => 4f * plotUiScale;

    /// <summary>Vertical gap below the plot bottom for X-axis labels.</summary>
    public static float AxisLabelGapBelowPlot(float plotUiScale) => 14f * plotUiScale;

    /// <summary>Vertical tweak for the top Y-axis label relative to the plot top.</summary>
    public static float AxisLabelOffsetAtPlotTop(float plotUiScale) => 10f * plotUiScale;

    /// <summary>
    /// Computes left reserve for Y-axis: numeric labels plus optional rotated axis title.
    /// Grows with axis font size so labels and title do not clip against the cell border.
    /// </summary>
    public static float ComputeYAxisReserveX(float plotUiScale, double axisFontSize, bool hasYAxisTitle)
    {
        var s = plotUiScale;
        var fontPx = (float)Math.Max(6d, axisFontSize) * s;
        var baseReserve = AxisReserveX * s;
        var tickLabelReserve = (fontPx * 3.2f) + (6f * s);
        if (!hasYAxisTitle)
        {
            return Math.Max(baseReserve, tickLabelReserve);
        }

        var yTitleCenterOffset = (AxisLabelInsetFromPlotLeft(s) * YAxisTitleInsetMultiplier) + fontPx + (6f * s);
        return Math.Max(Math.Max(baseReserve, tickLabelReserve), yTitleCenterOffset);
    }

    /// <summary>
    /// Computes bottom reserve for X-axis: tick labels plus optional axis title.
    /// Grows with axis font size so title/unit/labels do not overlap.
    /// </summary>
    public static float ComputeXAxisReserveY(float plotUiScale, double axisFontSize, bool hasXAxisTitle)
    {
        var s = plotUiScale;
        var fontPx = (float)Math.Max(6d, axisFontSize) * s;
        var baseReserve = AxisReserveY * s;
        var tickBand = AxisLabelGapBelowPlot(s) + (fontPx * 0.2f);
        if (!hasXAxisTitle)
        {
            return Math.Max(baseReserve, tickBand + (2f * s));
        }

        var titleBand = (AxisLabelGapBelowPlot(s) * 1.6f) + (fontPx * 0.95f) + (4f * s);
        return Math.Max(baseReserve, titleBand);
    }

    public static SKRect ComputePlotRect(
        int surfaceWidth,
        int surfaceHeight,
        float plotUiScale,
        bool useSparklineLayout,
        bool showXAxis,
        bool showYAxis,
        double axisFontSize,
        bool hasYAxisTitle,
        bool hasXAxisTitle)
    {
        var s = plotUiScale;
        var pad = CellPadding * s;
        var ax = showYAxis ? ComputeYAxisReserveX(s, axisFontSize, hasYAxisTitle) : 0f;
        var ay = showXAxis ? ComputeXAxisReserveY(s, axisFontSize, hasXAxisTitle) : 0f;
        if (useSparklineLayout)
        {
            return new SKRect(pad, pad, surfaceWidth - pad, surfaceHeight - pad);
        }

        return new SKRect(pad + ax, pad, surfaceWidth - pad, surfaceHeight - pad - ay);
    }
}
