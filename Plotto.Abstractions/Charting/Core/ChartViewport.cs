namespace Plotto.Charting.Core;

public sealed class ChartViewport
{
    public double XMin { get; set; }
    public double XMax { get; set; } = 1d;
    public double YMin { get; set; }
    public double YMax { get; set; } = 1d;

    public bool IsValid => XMax > XMin && YMax > YMin;

    public ChartViewport Clone()
    {
        return new ChartViewport
        {
            XMin = XMin,
            XMax = XMax,
            YMin = YMin,
            YMax = YMax
        };
    }

    public void EnsureMinimumSize(double minWidth = 1e-9, double minHeight = 1e-9)
    {
        if (XMax - XMin < minWidth)
        {
            var center = (XMax + XMin) * 0.5;
            XMin = center - (minWidth * 0.5);
            XMax = center + (minWidth * 0.5);
        }

        if (YMax - YMin < minHeight)
        {
            var center = (YMax + YMin) * 0.5;
            YMin = center - (minHeight * 0.5);
            YMax = center + (minHeight * 0.5);
        }
    }
}
