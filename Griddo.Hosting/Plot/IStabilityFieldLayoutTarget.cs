using Griddo.Hosting.Configuration;

namespace Griddo.Hosting.Plot;

public interface IStabilityFieldLayoutTarget
{
    string Label { get; set; }
    List<StabilitySeriesConfiguration> Series { get; set; }
}
