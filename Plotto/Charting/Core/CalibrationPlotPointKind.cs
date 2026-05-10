namespace Plotto.Charting.Core;

/// <summary>
/// Distinguishes calibration markers for styling and interaction (e.g. QC vs current sample overlays).
/// </summary>
public enum CalibrationPlotPointKind
{
    /// <summary>Bracket calibration level (toggle affects curve fit).</summary>
    CalibrationStandard = 0,

    /// <summary>Quality control sample overlay (not toggled for bracket fit).</summary>
    QualityControl = 1,

    /// <summary>Currently selected sample overlay (not toggled for bracket fit).</summary>
    CurrentSample = 2,
}
