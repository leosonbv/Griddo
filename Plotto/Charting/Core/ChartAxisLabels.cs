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

    /// <summary>
    /// Formats a tick using the grid <paramref name="step"/> so labels stay on clean decimals
    /// (avoids float noise like 0.30000000000000004). Custom <paramref name="format"/> still applies to the snapped value.
    /// </summary>
    public static string FormatRoundedTick(double tick, double step, int precisionFallback, string? unit, string? format)
    {
        var value = tick;
        if (step > 0 && double.IsFinite(step) && double.IsFinite(tick))
        {
            var places = DecimalPlacesForTickStep(step);
            var snapped = Math.Round(tick / step) * step;
            value = Math.Round(snapped, places);
        }

        if (!string.IsNullOrWhiteSpace(format))
        {
            return FormatTick(value, Math.Clamp(precisionFallback, 0, 10), unit, format);
        }

        var p = step > 0 && double.IsFinite(step)
            ? DecimalPlacesForTickStep(step)
            : Math.Clamp(precisionFallback, 0, 10);
        return FormatTick(value, p, unit, null);
    }

    public static int DecimalPlacesForTickStep(double step)
    {
        if (step <= 0 || !double.IsFinite(step))
        {
            return 2;
        }

        var places = (int)Math.Ceiling(-Math.Log10(step));
        return Math.Clamp(places, 0, 10);
    }

    public static IReadOnlyList<double> GetRoundedTicks(double min, double max, int maxTickCount = 6) =>
        GetRoundedTicks(min, max, maxTickCount, out _);

    public static IReadOnlyList<double> GetRoundedTicks(double min, double max, int maxTickCount, out double step)
    {
        step = 0;
        if (!double.IsFinite(min) || !double.IsFinite(max))
        {
            return [];
        }

        if (max < min)
        {
            (min, max) = (max, min);
        }

        var span = max - min;
        if (span <= 1e-12)
        {
            return [min];
        }

        // Do not cap the caller's requested budget at a small constant (e.g. 12): axis auto-fit
        // passes a width-derived desired count; capping here made every request ≥12 identical.
        var clampedMaxTicks = Math.Clamp(maxTickCount, 2, 128);
        step = NiceStep(span / (clampedMaxTicks - 1));
        if (step <= 0 || !double.IsFinite(step))
        {
            step = 0;
            return [min, max];
        }

        var start = Math.Ceiling(min / step) * step;
        var end = Math.Floor(max / step) * step;
        var ticks = new List<double>(clampedMaxTicks + 2);
        for (var v = start; v <= end + (step * 1e-6); v += step)
        {
            var rounded = Math.Abs(v) < (step * 1e-9) ? 0d : v;
            ticks.Add(rounded);
            if (ticks.Count > 200)
            {
                break;
            }
        }

        if (ticks.Count == 0)
        {
            step = 0;
            return [min, max];
        }

        return ticks;
    }

    private static double NiceStep(double roughStep)
    {
        if (roughStep <= 0 || !double.IsFinite(roughStep))
        {
            return 0d;
        }

        var exponent = Math.Floor(Math.Log10(roughStep));
        var fraction = roughStep / Math.Pow(10d, exponent);
        double niceFraction;
        if (fraction <= 1d)
        {
            niceFraction = 1d;
        }
        else if (fraction <= 2d)
        {
            niceFraction = 2d;
        }
        else if (fraction <= 5d)
        {
            niceFraction = 5d;
        }
        else
        {
            niceFraction = 10d;
        }

        return niceFraction * Math.Pow(10d, exponent);
    }
}
