using SkiaSharp;
using Plotto.Charting.Core;
using Plotto.Charting.Rendering;

namespace Plotto.Charting.Controls;

public class SpectrumControl : SkiaChartBaseControl
{
    private readonly SKPaint _stickPaint = new() { IsAntialias = false, StrokeWidth = 1f, Style = SKPaintStyle.Stroke, Color = SKColors.MediumPurple };

    protected override void ApplyUiScaleToResources()
    {
        base.ApplyUiScaleToResources();
        _stickPaint.StrokeWidth = Math.Max(0.5f, 1f * PlotUiScale);
    }

    protected override void DrawSeries(SKCanvas canvas, IReadOnlyList<ChartPoint> points, SKRect plotRect) =>
        ChartSkiaSpectrumSticks.DrawSticks(canvas, points, plotRect, Viewport.YMin, ToPixelX, ToPixelY, _stickPaint);
}
