using Plotto.Abstractions.Charting.Core;

namespace Griddo.Hosting.Plot;

/// <summary>Public helpers for remembering and broadcasting hosted plot viewports.</summary>
public static class HostedPlotViewportHelper
{
    public static string PlotKey(string sourceMemberName, string header) =>
        HostedPlotViewportMemory.PlotKey(sourceMemberName, header);

    public static void Remember(
        object? row,
        string plotKey,
        ChartViewport viewport,
        Func<object?, string?>? recordKeyFactory) =>
        HostedPlotViewportMemory.Remember(row, plotKey, viewport, recordKeyFactory);
}
