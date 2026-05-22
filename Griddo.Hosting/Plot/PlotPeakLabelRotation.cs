using System.Globalization;

namespace Griddo.Hosting.Plot;

public static class PlotPeakLabelRotation
{
    public const int StepDegrees = 15;

    public static readonly IReadOnlyList<int> DegreeOptions =
        Enumerable.Range(0, 360 / StepDegrees).Select(static i => i * StepDegrees).ToArray();

    public static readonly IReadOnlyList<string> DegreeOptionStrings =
        DegreeOptions.Select(static d => d.ToString(CultureInfo.InvariantCulture)).ToArray();

    public static int Normalize(int degrees)
    {
        degrees = ((degrees % 360) + 360) % 360;
        var snapped = (int)Math.Round(degrees / (double)StepDegrees, MidpointRounding.AwayFromZero) * StepDegrees;
        return snapped >= 360 ? 0 : snapped;
    }

    public static int FromLegacyBool(bool rotate) => rotate ? 90 : 0;
}
