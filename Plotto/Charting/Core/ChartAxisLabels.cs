namespace Plotto.Charting.Core;

/// <summary>
/// Axis tick visibility and text formatting (SRP: no Skia / WPF types).
/// </summary>
public static class ChartAxisLabels
{
    /// <summary>Non-negative axis tick values only; negative extrema are not labelled.</summary>
    public static bool ShouldDrawTickLabel(double value, double tolerance = 1e-12) => value >= -tolerance;

    public static string FormatTick(double value) => value.ToString("0.##");
}
