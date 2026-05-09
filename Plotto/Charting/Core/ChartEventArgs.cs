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

public sealed class PeakSplitEventArgs : EventArgs
{
    public PeakSplitEventArgs(double splitX)
    {
        SplitX = splitX;
    }

    public double SplitX { get; }
}

public sealed class PeakSelectionEventArgs : EventArgs
{
    public PeakSelectionEventArgs(double x)
    {
        X = x;
    }

    public double X { get; }
}

public sealed class CalibrationPointEventArgs : EventArgs
{
    public CalibrationPointEventArgs(CalibrationPoint point)
    {
        Point = point;
    }

    public CalibrationPoint Point { get; }
}
