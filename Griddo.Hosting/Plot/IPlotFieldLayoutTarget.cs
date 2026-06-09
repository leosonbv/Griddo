namespace Griddo.Hosting.Plot;

/// <summary>
/// Aggregate interface for all plot layout targets.
/// for existing code while allowing consumers to depend only on the segregated
/// <see cref="IPlotLayoutTarget"/>, <see cref="IChromatogramPlotLayoutTarget"/>,
/// <see cref="ICalibrationPlotLayoutTarget"/> or <see cref="ISpectrumPlotLayoutTarget"/>
/// when appropriate (ISP).
/// </summary>
public interface IPlotFieldLayoutTarget :
    IPlotLayoutTarget,
    IChromatogramPlotLayoutTarget,
    ICalibrationPlotLayoutTarget,
    ISpectrumPlotLayoutTarget
{
}
