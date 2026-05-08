using Plotto.Charting.Core;

namespace Griddo.Hosting.Abstractions;

public readonly record struct SignalPoint(double X, double Y);

public readonly record struct CalibrationSignalPoint(double X, double Y, bool Enabled = true);

public interface IChromatogramSignalProvider
{
    IReadOnlyList<SignalPoint> GetPoints(object recordSource);

    /// <summary>Peak integration bands drawn when chromatogram &quot;Show peak overlay&quot; is enabled (renderer mode only).</summary>
    IReadOnlyList<IntegrationRegion> GetPeakOverlayRegions(object recordSource) => [];
}

public interface ISpectrumSignalProvider
{
    IReadOnlyList<SignalPoint> GetPoints(object recordSource);
}

public interface ICalibrationSignalProvider
{
    IReadOnlyList<CalibrationSignalPoint> GetPoints(object recordSource);
    CalibrationFitMode GetFitMode(object recordSource);
}
