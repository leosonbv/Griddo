using SkiaSharp;
using Plotto.Charting.Core;

namespace Plotto.Charting.Controls;

public class SpectrumControl : SkiaChartBaseControl
{
    private readonly SKPaint _stickPaint = new() { IsAntialias = false, StrokeWidth = 1f, Style = SKPaintStyle.Stroke, Color = SKColors.MediumPurple };

    protected override void ApplyUiScaleToResources()
    {
        base.ApplyUiScaleToResources();
        _stickPaint.StrokeWidth = Math.Max(0.5f, 1f * PlotUiScale);
    }

    protected override void DrawSeries(SKCanvas canvas, IReadOnlyList<ChartPoint> points, SKRect plotRect)
    {
        if (points.Count == 0)
        {
            return;
        }

        var baseline = ToPixelY(Viewport.YMin, plotRect);
        var count = Math.Min(points.Count, Math.Max(200, (int)plotRect.Width));
        var step = (double)points.Count / count;
        for (var i = 0d; i < points.Count; i += step)
        {
            var point = points[(int)i];
            var x = ToPixelX(point.X, plotRect);
            var y = ToPixelY(point.Y, plotRect);
            canvas.DrawLine(x, baseline, x, y, _stickPaint);
        }
    }
}
