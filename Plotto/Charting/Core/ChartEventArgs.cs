namespace Plotto.Charting.Core;

public sealed class ChartPointEventArgs : EventArgs
{
    public ChartPointEventArgs(ChartPoint point)
    {
        Point = point;
    }

    public ChartPoint Point { get; }
}

public sealed class IntegrationRegionEventArgs : EventArgs
{
    public IntegrationRegionEventArgs(IntegrationRegion region)
    {
        Region = region;
    }

    public IntegrationRegion Region { get; }
}

public sealed class CalibrationPointEventArgs : EventArgs
{
    public CalibrationPointEventArgs(CalibrationPoint point)
    {
        Point = point;
    }

    public CalibrationPoint Point { get; }
}
