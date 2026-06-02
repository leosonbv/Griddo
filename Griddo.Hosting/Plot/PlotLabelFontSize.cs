namespace Griddo.Hosting.Plot;

internal static class PlotLabelFontSize
{
    internal const double Default = 13d;

    internal static double ResolvePeakLabelFontSize(double configured, double axisFontSize) =>
        configured > 0
            ? Math.Clamp(configured, 6d, 96d)
            : Default;
}
