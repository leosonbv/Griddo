using Plotto.Charting.Core;

namespace Griddo.Hosting.Abstractions;

public readonly record struct SignalPoint(double X, double Y);

public readonly record struct CalibrationSignalPoint(double X, double Y, bool Enabled = true);
public readonly record struct ChromatogramPeakOverlayRegion(
    IntegrationRegion Region,
    bool IsSelected,
    bool IsManualIntegrated = false);

public interface ITicPeakOverlayOptions
{
    bool OverlayIstdPeaks { get; set; }
    bool OverlaySurrogatePeaks { get; set; }
    bool OverlayTargetPeaks { get; set; }
}

public interface IChromatogramSignalProvider
{
    IReadOnlyList<SignalPoint> GetPoints(object recordSource);

    /// <summary>Peak integration bands drawn when chromatogram &quot;Show peak overlay&quot; is enabled (renderer mode only).</summary>
    IReadOnlyList<IntegrationRegion> GetPeakOverlayRegions(object recordSource) => [];

    /// <summary>Peak integration bands with selection state (selected vs alternative).</summary>
    IReadOnlyList<ChromatogramPeakOverlayRegion> GetPeakOverlayRegionsWithSelection(object recordSource) =>
        GetPeakOverlayRegions(recordSource)
            .Select(static region => new ChromatogramPeakOverlayRegion(region, IsSelected: false, IsManualIntegrated: false))
            .ToList();

    /// <summary>Optional per-region colored overlays (used for category-specific peak coloring, e.g. TIC).</summary>
    IReadOnlyList<ColoredIntegrationRegion> GetPeakOverlayRegionsColored(object recordSource) => [];

    /// <summary>Called when user draws a manual integration region in editor mode.</summary>
    bool TryApplyManualIntegration(object recordSource, IntegrationRegion region) => false;

    /// <summary>Called when user requests manual peak split (Ctrl+left click) in editor mode.</summary>
    bool TryApplyPeakSplit(object recordSource, double splitX) => false;

    /// <summary>Called when user left-clicks at X and wants to select the peak under the cursor.</summary>
    bool TrySelectPeakAtX(object recordSource, double x) => false;
}

public interface ISpectrumSignalProvider
{
    IReadOnlyList<SignalPoint> GetPoints(object recordSource);
}

public interface ICalibrationSignalProvider
{
    IReadOnlyList<CalibrationSignalPoint> GetPoints(object recordSource);
    CalibrationFitMode GetFitMode(object recordSource);

    /// <summary>
    /// Optional fitted curve in the same X/Y space as <see cref="GetPoints"/> (e.g. Quanto bracket curve).
    /// When null or too few samples, the chart may draw a simple point polyline only.
    /// </summary>
    IReadOnlyList<SignalPoint>? GetCurveLineSamples(object recordSource, int segmentCount) => null;

    /// <summary>
    /// Called when the user toggles a calibration point in editor mode (same concentration/response coordinates as <see cref="GetPoints"/>).
    /// Implementations should persist the change, refit the curve, and return true when handled.
    /// </summary>
    bool TryToggleCalibrationPoint(object recordSource, double x, double y, bool enabled) => false;
}
