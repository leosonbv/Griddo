namespace Plotto.Charting.Core;

/// <summary>
/// Vertical reference line in chromatogram plot coordinates (same X axis as the trace).
/// <see cref="YStartFraction"/> and <see cref="YEndFraction"/> are fractions of plot height from the top (0 = top, 1 = bottom).
/// </summary>
public readonly record struct ChromatogramVerticalMarker(
    double X,
    float YStartFraction,
    float YEndFraction,
    bool LightStroke = false);
