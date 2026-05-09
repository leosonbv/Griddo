using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Plotto.Charting.Controls;
using Plotto.Charting.Core;

namespace Griddo.Hosting.Plot;

/// <summary>
/// Session-local zoom/pan: keyed by an optional stable logical row id (see hosted field <c>ViewportZoomRecordKey</c>)
/// and by plot column (<see cref="PlotKey"/>). Falls back to runtime hash if no factory is supplied.
/// </summary>
internal static class HostedPlotViewportMemory
{
    /// <summary>Logical row ids often include file paths and compound names; match case-insensitively like grid joins.</summary>
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ChartViewport>> Store =
        new(StringComparer.OrdinalIgnoreCase);

    internal static string PlotKey(string sourceMemberName, string header) =>
        !string.IsNullOrEmpty(sourceMemberName) ? sourceMemberName : header;

    internal static string ResolveRowKey(object? row, Func<object?, string?>? recordKeyFactory)
    {
        if (row is null)
        {
            return string.Empty;
        }

        var logical = recordKeyFactory?.Invoke(row);
        if (!string.IsNullOrEmpty(logical))
        {
            return logical;
        }

        return "object:" + RuntimeHelpers.GetHashCode(row);
    }

    internal static void Remember(object? row, string plotKey, ChartViewport viewport, Func<object?, string?>? recordKeyFactory)
    {
        if (string.IsNullOrEmpty(plotKey) || !viewport.IsValid)
        {
            return;
        }

        var rowKey = ResolveRowKey(row, recordKeyFactory);
        if (string.IsNullOrEmpty(rowKey))
        {
            return;
        }

        var map = Store.GetOrAdd(rowKey, static _ => new ConcurrentDictionary<string, ChartViewport>(StringComparer.Ordinal));
        map[plotKey] = viewport.Clone();
    }

    /// <summary>
    /// Runs after the current layout/render pass so restoring wins over synchronous data callbacks
    /// (e.g. <see cref="SkiaChartBaseControl"/> point changed → fit viewport) when those queue further work.
    /// </summary>
    internal static void ScheduleDeferredTryRestore(
        FrameworkElement host,
        object recordSource,
        string plotKey,
        SkiaChartBaseControl chart,
        Func<object?, string?>? recordKeyFactory)
    {
        var expectedRow = recordSource;
        chart.Dispatcher.BeginInvoke(
            () =>
            {
                if (!ReferenceEquals(host.Tag, expectedRow))
                {
                    return;
                }

                TryRestore(expectedRow, plotKey, chart, recordKeyFactory);
            },
            DispatcherPriority.Render);
    }

    internal static void SaveLeavingRow(object? previousRow, string plotKey, ChartViewport viewport, Func<object?, string?>? recordKeyFactory) =>
        Remember(previousRow, plotKey, viewport, recordKeyFactory);

    internal static bool TryRestore(object? row, string plotKey, SkiaChartBaseControl chart, Func<object?, string?>? recordKeyFactory)
    {
        if (row is null || string.IsNullOrEmpty(plotKey) || chart.Points.Count == 0)
        {
            return false;
        }

        var rowKey = ResolveRowKey(row, recordKeyFactory);
        if (string.IsNullOrEmpty(rowKey))
        {
            return false;
        }

        if (!Store.TryGetValue(rowKey, out var map))
        {
            return false;
        }

        if (!map.TryGetValue(plotKey, out var cached) || cached is null || !cached.IsValid)
        {
            return false;
        }

        chart.RestoreViewportSnapshot(cached);
        return true;
    }
}
