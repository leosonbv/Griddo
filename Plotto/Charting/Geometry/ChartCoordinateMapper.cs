using System.Windows;
using Plotto.Charting.Core;
using SkiaSharp;

namespace Plotto.Charting.Geometry;

/// <summary>
/// Single responsibility: DIP ↔ Skia surface pixel ↔ data coordinates using a fixed <see cref="ChartViewport"/> and plot rect.
/// </summary>
public sealed class ChartCoordinateMapper
{
    private readonly ChartViewport _viewport;
    private int _surfacePixelWidth = 1;
    private int _surfacePixelHeight = 1;
    private SKRect _plotRect;
    private SKRect _seriesPlotRect;
    private double _hitTestSyncedActualWidth = double.NaN;
    private double _hitTestSyncedActualHeight = double.NaN;

    public ChartCoordinateMapper(ChartViewport viewport) => _viewport = viewport;

    public SKRect PlotRect => _plotRect;

    /// <summary>Plot rectangle used for series coordinates and hit-testing (excludes chart title band when present).</summary>
    public SKRect SeriesPlotRect => _seriesPlotRect;

    public int SurfacePixelWidth => _surfacePixelWidth;

    public int SurfacePixelHeight => _surfacePixelHeight;

    public void ResetHitTestGeometrySync()
    {
        _hitTestSyncedActualWidth = double.NaN;
        _hitTestSyncedActualHeight = double.NaN;
    }

    public void MarkPaintLayoutSynced(double actualWidthDip, double actualHeightDip)
    {
        if (actualWidthDip > 0 && actualHeightDip > 0)
        {
            _hitTestSyncedActualWidth = actualWidthDip;
            _hitTestSyncedActualHeight = actualHeightDip;
        }
    }

    /// <summary>
    /// When hit-testing runs before the next paint, rebuild surface/plot state from layout so mapping matches the current cell.
    /// </summary>
    public void EnsureHitTestGeometryFromLayout(
        double actualWidthDip,
        double actualHeightDip,
        double pixelsPerDip,
        bool useSparklineLayout,
        float plotUiScale,
        bool showXAxis,
        bool showYAxis,
        double axisFontSize,
        bool hasYAxisTitle,
        bool hasXAxisTitle)
    {
        if (actualWidthDip <= 0 || actualHeightDip <= 0)
        {
            return;
        }

        if (!double.IsNaN(_hitTestSyncedActualWidth)
            && actualWidthDip == _hitTestSyncedActualWidth
            && actualHeightDip == _hitTestSyncedActualHeight)
        {
            return;
        }

        _hitTestSyncedActualWidth = actualWidthDip;
        _hitTestSyncedActualHeight = actualHeightDip;
        var pw = (int)Math.Max(1, Math.Round(actualWidthDip * pixelsPerDip));
        var ph = (int)Math.Max(1, Math.Round(actualHeightDip * pixelsPerDip));
        ApplySurfaceDimensions(pw, ph, useSparklineLayout, plotUiScale, showXAxis, showYAxis, axisFontSize, hasYAxisTitle, hasXAxisTitle);
    }

    public void ApplySurfaceDimensions(
        int surfaceWidth,
        int surfaceHeight,
        bool useSparklineLayout,
        float plotUiScale,
        bool showXAxis,
        bool showYAxis,
        double axisFontSize,
        bool hasYAxisTitle,
        bool hasXAxisTitle)
    {
        surfaceWidth = Math.Max(1, surfaceWidth);
        surfaceHeight = Math.Max(1, surfaceHeight);
        _surfacePixelWidth = surfaceWidth;
        _surfacePixelHeight = surfaceHeight;
        _plotRect = ChartPlotLayout.ComputePlotRect(surfaceWidth, surfaceHeight, plotUiScale, useSparklineLayout, showXAxis, showYAxis, axisFontSize, hasYAxisTitle, hasXAxisTitle);
        _seriesPlotRect = _plotRect;
    }

    /// <summary>
    /// Shrinks the coordinate-mapping plot from the top by <paramref name="topInsetPixels"/> (chart title band). Must run after <see cref="ApplySurfaceDimensions"/>.
    /// </summary>
    public void ApplyChartTitleTopInset(float topInsetPixels)
    {
        if (topInsetPixels <= 0.5f || topInsetPixels >= _plotRect.Height - 1f)
        {
            _seriesPlotRect = _plotRect;
            return;
        }

        _seriesPlotRect = new SKRect(_plotRect.Left, _plotRect.Top + topInsetPixels, _plotRect.Right, _plotRect.Bottom);
    }

    public Point LogicalPointToSurface(Point logical, double actualWidthDip, double actualHeightDip)
    {
        if (actualWidthDip <= 0 || actualHeightDip <= 0)
        {
            return logical;
        }

        return new Point(
            logical.X * _surfacePixelWidth / actualWidthDip,
            logical.Y * _surfacePixelHeight / actualHeightDip);
    }

    public ChartPoint SurfacePixelToChartPoint(Point surfacePixel)
    {
        var r = _seriesPlotRect;
        var x = _viewport.XMin + ((surfacePixel.X - r.Left) / Math.Max(1, r.Width)) * (_viewport.XMax - _viewport.XMin);
        var y = _viewport.YMin + ((r.Bottom - surfacePixel.Y) / Math.Max(1, r.Height)) * (_viewport.YMax - _viewport.YMin);
        return new ChartPoint(x, y);
    }
}
