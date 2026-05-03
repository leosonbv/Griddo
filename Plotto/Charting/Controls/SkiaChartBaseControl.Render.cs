using System.Windows;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Plotto.Charting.Core;
using Plotto.Charting.Geometry;
using Plotto.Charting.Rendering;

namespace Plotto.Charting.Controls;

public abstract partial class SkiaChartBaseControl
{
    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);
        DrawChart(e.Surface.Canvas, e.Info.Width, e.Info.Height);
        _coordinates.MarkPaintLayoutSynced(ActualWidth, ActualHeight);
    }

    protected virtual void DrawSeries(SKCanvas canvas, IReadOnlyList<ChartPoint> points, SKRect plotRect) =>
        ChartSkiaLineSeries.DrawPolyline(canvas, points, plotRect, ToPixelX, ToPixelY, _linePaint);

    protected virtual void DrawOverlay(SKCanvas canvas, SKRect plotRect)
    {
    }

    protected float ToPixelX(double x, SKRect plotRect)
    {
        return plotRect.Left + (float)((x - Viewport.XMin) / (Viewport.XMax - Viewport.XMin) * plotRect.Width);
    }

    protected float ToPixelY(double y, SKRect plotRect)
    {
        return plotRect.Bottom - (float)((y - Viewport.YMin) / (Viewport.YMax - Viewport.YMin) * plotRect.Height);
    }

    private void DrawChart(SKCanvas canvas, int width, int height)
    {
        canvas.Clear(SKColors.Transparent);

        if (width <= 0 || height <= 0)
        {
            return;
        }

        _coordinates.ApplySurfaceDimensions(width, height, UseSparklineLayout, PlotUiScale);

        if (PlotRect.Width <= 2 || PlotRect.Height <= 2)
        {
            return;
        }

        if (!Viewport.IsValid || Points.Count == 0)
        {
            UpdateViewportFromData();
        }

        canvas.Save();
        canvas.ClipRect(PlotRect);

        if (Points.Count > 0)
        {
            DrawSeries(canvas, Points, PlotRect);
        }

        DrawOverlay(canvas, PlotRect);

        canvas.Restore();

        if (!UseSparklineLayout)
        {
            DrawAxes(canvas, PlotRect);
        }

        if (_isRightDragZoom && CanInteract())
        {
            var x0 = (float)Math.Min(_zoomRectStart.X, _zoomRectCurrent.X);
            var y0 = (float)Math.Min(_zoomRectStart.Y, _zoomRectCurrent.Y);
            var x1 = (float)Math.Max(_zoomRectStart.X, _zoomRectCurrent.X);
            var y1 = (float)Math.Max(_zoomRectStart.Y, _zoomRectCurrent.Y);
            var rubber = new SKRect(x0, y0, x1, y1);
            canvas.DrawRect(rubber, _zoomRubberFillPaint);
            canvas.DrawRect(rubber, _zoomRubberStrokePaint);
        }
    }

    protected virtual void DrawAxes(SKCanvas canvas, SKRect plotRect)
    {
        var zs = PlotUiScale;
        var axOff = ChartPlotLayout.AxisLabelInsetFromPlotLeft(zs);
        var below = ChartPlotLayout.AxisLabelGapBelowPlot(zs);
        var topLab = ChartPlotLayout.AxisLabelOffsetAtPlotTop(zs);
        canvas.DrawLine(plotRect.Left, plotRect.Bottom, plotRect.Right, plotRect.Bottom, AxisStrokePaint);
        canvas.DrawLine(plotRect.Left, plotRect.Top, plotRect.Left, plotRect.Bottom, AxisStrokePaint);
        if (ChartAxisLabels.ShouldDrawTickLabel(Viewport.XMin))
        {
            canvas.DrawText(ChartAxisLabels.FormatTick(Viewport.XMin), plotRect.Left, plotRect.Bottom + below, SKTextAlign.Left, AxisFont, AxisLabelPaint);
        }

        if (ChartAxisLabels.ShouldDrawTickLabel(Viewport.XMax))
        {
            canvas.DrawText(ChartAxisLabels.FormatTick(Viewport.XMax), plotRect.Right, plotRect.Bottom + below, SKTextAlign.Right, AxisFont, AxisLabelPaint);
        }

        if (ChartAxisLabels.ShouldDrawTickLabel(Viewport.YMax))
        {
            canvas.DrawText(ChartAxisLabels.FormatTick(Viewport.YMax), plotRect.Left - axOff, plotRect.Top + topLab, SKTextAlign.Right, AxisFont, AxisLabelPaint);
        }

        if (ChartAxisLabels.ShouldDrawTickLabel(Viewport.YMin))
        {
            canvas.DrawText(ChartAxisLabels.FormatTick(Viewport.YMin), plotRect.Left - axOff, plotRect.Bottom, SKTextAlign.Right, AxisFont, AxisLabelPaint);
        }
    }

    protected void DrawFilledXRange(SKCanvas canvas, SKRect plotRect, double fromX, double toX) =>
        ChartSkiaXRangeHighlight.DrawFilledBand(canvas, plotRect, fromX, toX, ToPixelX, _overlayFill, _overlayStroke);
}
