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

    /// <summary>Horizontal offset for Y-axis numeric labels (DIP scale).</summary>
    public static float AxisLabelInsetFromPlotLeft(float plotUiScale) => 4f * plotUiScale;

    /// <summary>Vertical gap below the plot bottom for X-axis labels.</summary>
    public static float AxisLabelGapBelowPlot(float plotUiScale) => 14f * plotUiScale;

    /// <summary>Vertical tweak for the top Y-axis label relative to the plot top.</summary>
    public static float AxisLabelOffsetAtPlotTop(float plotUiScale) => 10f * plotUiScale;

    public static SKRect ComputePlotRect(int surfaceWidth, int surfaceHeight, float plotUiScale, bool useSparklineLayout)
    {
        var s = plotUiScale;
        var pad = CellPadding * s;
        var ax = AxisReserveX * s;
        var ay = AxisReserveY * s;
        if (useSparklineLayout)
        {
            return new SKRect(pad, pad, surfaceWidth - pad, surfaceHeight - pad);
        }

        return new SKRect(pad + ax, pad, surfaceWidth - pad, surfaceHeight - pad - ay);
    }
}
