namespace Plotto.Charting.Axes;

/// <summary>
/// Builds evenly spaced "nice" axis ticks (1/2/5 × 10^n steps). Pure data — no UI types.
/// </summary>
public static class NiceAxisTickGrid
{
    public static IReadOnlyList<double> Generate(double min, double max, int desiredTickCount = 6) =>
        Generate(min, max, desiredTickCount, out _);

    /// <param name="desiredTickCount">Target number of ticks (including ends); used only to derive step size.</param>
    public static IReadOnlyList<double> Generate(double min, double max, int desiredTickCount, out double step)
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

        var clampedDesired = Math.Clamp(desiredTickCount, 2, 128);
        step = NiceStep(span / (clampedDesired - 1));
        if (step <= 0 || !double.IsFinite(step))
        {
            step = 0;
            return [min, max];
        }

        var start = Math.Ceiling(min / step) * step;
        var end = Math.Floor(max / step) * step;
        var ticks = new List<double>(clampedDesired + 2);
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
