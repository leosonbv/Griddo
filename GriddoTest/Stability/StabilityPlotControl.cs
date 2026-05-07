using System.Windows;
using Plotto.Charting.Controls;
using Plotto.Charting.Core;
using SkiaSharp;

namespace GriddoTest.Stability;

public sealed class StabilityPlotControl : SkiaChartBaseControl
{
    private readonly SKPaint _sigmaPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1f,
        Color = new SKColor(200, 90, 90, 180),
        PathEffect = SKPathEffect.CreateDash([6f, 4f], 0f)
    };

    private readonly SKPaint _meanPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1.2f,
        Color = new SKColor(80, 120, 210, 220),
        PathEffect = SKPathEffect.CreateDash([2f, 2f], 0f)
    };

    public double MeanValue { get; set; }
    public double StandardDeviation { get; set; }
    public IReadOnlyList<double> SigmaMultipliers { get; set; } = [1, 2, 3];

    public void ZoomInX() => ZoomXAt(GetCenterPivot(), 0.9);
    public void ZoomOutX() => ZoomXAt(GetCenterPivot(), 1.1);
    public void ZoomInY() => ZoomYAt(GetCenterPivot(), 0.9);
    public void ZoomOutY() => ZoomYAt(GetCenterPivot(), 1.1);

    protected override void DrawOverlay(SKCanvas canvas, SKRect plotRect)
    {
        if (StandardDeviation <= 0)
        {
            return;
        }

        DrawHorizontal(canvas, plotRect, MeanValue, _meanPaint);
        foreach (var multiplier in SigmaMultipliers.Where(m => m > 0))
        {
            DrawHorizontal(canvas, plotRect, MeanValue + (multiplier * StandardDeviation), _sigmaPaint);
            DrawHorizontal(canvas, plotRect, MeanValue - (multiplier * StandardDeviation), _sigmaPaint);
        }
    }

    private Point GetCenterPivot() => new(ActualWidth * 0.5, ActualHeight * 0.5);

    private void DrawHorizontal(SKCanvas canvas, SKRect plotRect, double y, SKPaint paint)
    {
        if (y < Viewport.YMin || y > Viewport.YMax)
        {
            return;
        }

        var py = ToPixelY(y, plotRect);
        canvas.DrawLine(plotRect.Left, py, plotRect.Right, py, paint);
    }
}
