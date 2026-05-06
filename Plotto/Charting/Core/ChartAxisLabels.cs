using System.Globalization;

namespace Plotto.Charting.Core;

/// <summary>
/// Axis tick visibility and text formatting (SRP: no Skia / WPF types).
/// </summary>
public static class ChartAxisLabels
{
    /// <summary>Non-negative axis tick values only; negative extrema are not labelled.</summary>
    public static bool ShouldDrawTickLabel(double value, double tolerance = 1e-12) => value >= -tolerance;

    public static string FormatTick(double value, int precision = 2, string? unit = null, string? format = null)
    {
        var text = string.Empty;
        if (!string.IsNullOrWhiteSpace(format))
        {
            try
            {
                text = value.ToString(format, CultureInfo.CurrentCulture);
            }
            catch
            {
                text = string.Empty;
            }
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            var p = Math.Clamp(precision, 0, 10);
            text = value.ToString($"0.{new string('#', p)}", CultureInfo.CurrentCulture);
        }

        if (!string.IsNullOrWhiteSpace(unit))
        {
            text = $"{text} {unit.Trim()}";
        }

        return text;
    }
}
