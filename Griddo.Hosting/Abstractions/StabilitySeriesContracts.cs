namespace Griddo.Hosting.Abstractions;

public readonly record struct StabilityPoint(double AcquisitionTime, double Value);

public interface IStabilitySeriesProvider
{
    IReadOnlyList<StabilityPoint> GetPoints(object recordSource);
}
