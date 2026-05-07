using System.Globalization;

namespace Plotto.Charting.Axes;

/// <summary>
/// Culture-aware numeric strings for axis ticks (no Skia types).
/// </summary>
public static class AxisTickLabelFormatter
{
    public static string FormatNumber(double value, int precision = 2, string? unit = null, string? format = null)
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

    /// <summary>
    /// Snaps <paramref name="tick"/> to the grid defined by <paramref name="step"/> before formatting
    /// so labels avoid binary float noise.
    /// </summary>
    public static string FormatSnappedToGrid(double tick, double step, int precisionFallback, string? unit, string? format)
    {
        var value = tick;
        if (step > 0 && double.IsFinite(step) && double.IsFinite(tick))
        {
            var places = DecimalPlacesForGridStep(step);
            var snapped = Math.Round(tick / step) * step;
            value = Math.Round(snapped, places);
        }

        if (!string.IsNullOrWhiteSpace(format))
        {
            return FormatNumber(value, Math.Clamp(precisionFallback, 0, 10), unit, format);
        }

        var p = step > 0 && double.IsFinite(step)
            ? DecimalPlacesForGridStep(step)
            : Math.Clamp(precisionFallback, 0, 10);
        return FormatNumber(value, p, unit, null);
    }

    public static int DecimalPlacesForGridStep(double step)
    {
        if (step <= 0 || !double.IsFinite(step))
        {
            return 2;
        }

        var places = (int)Math.Ceiling(-Math.Log10(step));
        return Math.Clamp(places, 0, 10);
    }
}
