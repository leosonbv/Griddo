namespace Plotto.Charting.Axes;

/// <summary>
/// Axis labels are shown only for tick values that are not negative (origin-focused charts).
/// </summary>
public static class NonNegativeAxisTickPolicy
{
    public static bool AllowsLabel(double value, double tolerance = 1e-12) => value >= -tolerance;
}
