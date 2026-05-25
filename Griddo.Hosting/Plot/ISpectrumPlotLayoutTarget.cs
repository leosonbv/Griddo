namespace Griddo.Hosting.Plot;

/// <summary>
/// Spectrum plot specific layout options.
/// Segregated per ISP.
/// </summary>
public interface ISpectrumPlotLayoutTarget
{
    bool SpectrumNormalizeIntensity { get; set; }
}
