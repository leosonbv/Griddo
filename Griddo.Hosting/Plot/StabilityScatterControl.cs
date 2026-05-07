using Griddo.Hosting.Configuration;
using Plotto.Charting.Axes;
using Plotto.Charting.Controls;
using Plotto.Charting.Core;
using SkiaSharp;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Griddo.Hosting.Plot;

public sealed class StabilityScatterControl : SkiaChartBaseControl
{
    private const float RightAxisGapDip = 4f;
    private readonly SKPaint _seriesLinePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f };
    private readonly SKPaint _seriesMarkerPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _meanPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f };
    private readonly SKPaint _sdPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.1f, PathEffect = SKPathEffect.CreateDash([6f, 4f], 0f) };
    private readonly SKPaint _rightAxisPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = new SKColor(120, 120, 120, 200) };
    private readonly SKPaint _rightAxisTextPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill, TextSize = 11f, Color = new SKColor(90, 90, 90, 220) };

    private readonly List<SeriesData> _series = [];
    private bool _hasRightAxis;
    private bool _hasLeftAxis;
    private SKColor _leftAxisColor = new(120, 120, 120, 220);
    private double _rightYMin;
    private double _rightYMax;
    private double _rightViewYMin;
    private double _rightViewYMax;
    private SKColor _rightAxisColor = new(120, 120, 120, 220);
    private StabilityAxisSide _activeAxisSide = StabilityAxisSide.Left;

    public bool HasLeftAxis => _hasLeftAxis;
    public bool HasRightAxis => _hasRightAxis;
    public StabilityAxisSide ActiveAxisSide => _activeAxisSide;

    public void SetSeries(IReadOnlyList<SeriesData> series)
    {
        _series.Clear();
        _series.AddRange(series.Where(static s => s.Points.Count > 0));
        _hasLeftAxis = _series.Any(static s => s.AxisSide is StabilityAxisSide.Left or StabilityAxisSide.Both);
        Points = _series.SelectMany(static s => s.Points).ToArray();
        RecalculateRightAxisRange();
    }

    protected override void DrawSeries(SKCanvas canvas, IReadOnlyList<ChartPoint> points, SKRect plotRect)
    {
        _ = points;
        var dataRect = GetDataPlotRect(plotRect);
        foreach (var series in _series)
        {
            var color = ParseColor(series.Color, SKColors.SteelBlue.WithAlpha(220));
            _seriesLinePaint.Color = color;
            _seriesMarkerPaint.Color = color;
            _meanPaint.Color = color.WithAlpha(220);
            _sdPaint.Color = color.WithAlpha(180);

            if (series.AxisSide is StabilityAxisSide.Left or StabilityAxisSide.Both)
            {
                DrawSeriesPoints(canvas, dataRect, series);
                if (series.ShowSdLines && series.StandardDeviation > 0)
                {
                    DrawSigmaLines(canvas, dataRect, series, StabilityAxisSide.Left);
                }
            }

            if (series.AxisSide is StabilityAxisSide.Right or StabilityAxisSide.Both)
            {
                DrawSeriesPoints(canvas, dataRect, CreateAxisProjection(series, StabilityAxisSide.Right));
                if (series.ShowSdLines && series.StandardDeviation > 0)
                {
                    DrawSigmaLines(canvas, dataRect, series, StabilityAxisSide.Right);
                }
            }
        }
    }

    protected override void DrawOverlay(SKCanvas canvas, SKRect plotRect)
    {
        if (_hasRightAxis)
        {
            DrawRightAxis(canvas, GetDataPlotRect(plotRect), plotRect);
        }
    }

    protected override void DrawAxes(SKCanvas canvas, SKRect plotRect)
    {
        var dataRect = GetDataPlotRect(plotRect);
        AxisStrokePaint.Color = SKColors.Gray;
        AxisLabelPaint.Color = SKColors.Gray;
        base.DrawAxes(canvas, dataRect);
        if (_hasLeftAxis)
        {
            DrawLeftAxisColored(canvas, dataRect);
        }
    }

    public void ZoomOutYToPoints(bool includeZero)
    {
        if (_series.Count == 0)
        {
            return;
        }

        if (_hasLeftAxis)
        {
            var yMin = _series.Where(static s => s.AxisSide is StabilityAxisSide.Left or StabilityAxisSide.Both).SelectMany(static s => s.Points).DefaultIfEmpty(new ChartPoint(0, 0)).Min(static p => p.Y);
            var yMax = _series.Where(static s => s.AxisSide is StabilityAxisSide.Left or StabilityAxisSide.Both).SelectMany(static s => s.Points).DefaultIfEmpty(new ChartPoint(0, 0)).Max(static p => p.Y);
            if (includeZero)
            {
                yMin = Math.Min(yMin, 0);
                yMax = Math.Max(yMax, 0);
            }

            var pad = Math.Max(1e-6, (yMax - yMin) * 0.05);
            Viewport.YMin = yMin - pad;
            Viewport.YMax = yMax + pad;
            Viewport.EnsureMinimumSize();
        }

        if (_hasRightAxis)
        {
            ZoomOutRightYToPoints(includeZero);
        }
        InvalidateVisual();
    }

    public void ZoomOutActiveYToPoints(bool includeZero)
    {
        if (_activeAxisSide == StabilityAxisSide.Right && _hasRightAxis)
        {
            ZoomOutRightYToPoints(includeZero);
            return;
        }

        ZoomOutYToPoints(includeZero);
    }

    public void ZoomActiveYToZeroToMaxPlusFivePercent()
    {
        if (_activeAxisSide == StabilityAxisSide.Right && _hasRightAxis)
        {
            ZoomRightYToZeroToMaxPlusFivePercent();
            return;
        }

        ZoomLeftYToZeroToMaxPlusFivePercent();
    }

    public void ZoomOutXToPoints()
    {
        if (_series.Count == 0)
        {
            return;
        }

        var xMin = _series.SelectMany(static s => s.Points).Min(static p => p.X);
        var xMax = _series.SelectMany(static s => s.Points).Max(static p => p.X);
        var pad = Math.Max(1e-6, (xMax - xMin) * 0.05);
        Viewport.XMin = xMin - pad;
        Viewport.XMax = xMax + pad;
        Viewport.EnsureMinimumSize();
        InvalidateVisual();
    }

    public void ZoomOutAllToPoints(bool includeZero)
    {
        ZoomOutXToPoints();
        ZoomOutYToPoints(includeZero);
    }

    public void ZoomYToZeroToPlus3Sd()
    {
        var sigma = _series.Where(static s => s.ShowSdLines && s.AxisSide is StabilityAxisSide.Left or StabilityAxisSide.Both).Select(static s => s.Mean + (3d * s.StandardDeviation)).DefaultIfEmpty(0d).Max();
        var upperPad = Math.Max(1e-6, Math.Abs(sigma) * 0.05);
        Viewport.YMin = 0d;
        Viewport.YMax = sigma + upperPad;
        Viewport.EnsureMinimumSize();
        InvalidateVisual();
    }

    public void ZoomLeftYToZeroToMaxPlusFivePercent()
    {
        if (!_hasLeftAxis)
        {
            return;
        }

        var upper = _series
            .Where(static s => s.AxisSide is StabilityAxisSide.Left or StabilityAxisSide.Both)
            .SelectMany(static s => s.Points)
            .DefaultIfEmpty(new ChartPoint(0, 0))
            .Max(static p => p.Y);
        var pad = Math.Max(1e-6, Math.Abs(upper) * 0.05);
        Viewport.YMin = 0d;
        Viewport.YMax = upper + pad;
        Viewport.EnsureMinimumSize();
        InvalidateVisual();
    }

    public void ZoomActiveYToZeroToPlus3Sd()
    {
        if (_activeAxisSide == StabilityAxisSide.Right && _hasRightAxis)
        {
            ZoomRightYToZeroToPlus3Sd();
            return;
        }

        ZoomYToZeroToPlus3Sd();
    }

    public void ZoomYToPlusMinus3Sd()
    {
        var upper = _series.Where(static s => s.ShowSdLines && s.AxisSide is StabilityAxisSide.Left or StabilityAxisSide.Both).Select(static s => s.Mean + (3d * s.StandardDeviation)).DefaultIfEmpty(0d).Max();
        var lower = _series.Where(static s => s.ShowSdLines && s.AxisSide is StabilityAxisSide.Left or StabilityAxisSide.Both).Select(static s => s.Mean - (3d * s.StandardDeviation)).DefaultIfEmpty(0d).Min();
        var pad = Math.Max(1e-6, (upper - lower) * 0.05);
        Viewport.YMin = lower - pad;
        Viewport.YMax = upper + pad;
        Viewport.EnsureMinimumSize();
        InvalidateVisual();
    }

    public void ZoomActiveYToPlusMinus3Sd()
    {
        if (_activeAxisSide == StabilityAxisSide.Right && _hasRightAxis)
        {
            ZoomRightYToPlusMinus3Sd();
            return;
        }

        ZoomYToPlusMinus3Sd();
    }

    public void ZoomOutRightYToPoints(bool includeZero)
    {
        if (!_hasRightAxis)
        {
            return;
        }

        var yMin = _series.Where(static s => s.AxisSide is StabilityAxisSide.Right or StabilityAxisSide.Both).SelectMany(static s => s.Points).Min(static p => p.Y);
        var yMax = _series.Where(static s => s.AxisSide is StabilityAxisSide.Right or StabilityAxisSide.Both).SelectMany(static s => s.Points).Max(static p => p.Y);
        if (includeZero)
        {
            yMin = Math.Min(yMin, 0);
            yMax = Math.Max(yMax, 0);
        }

        var pad = Math.Max(1e-6, (yMax - yMin) * 0.05);
        _rightViewYMin = yMin - pad;
        _rightViewYMax = yMax + pad;
        EnsureRightMinimumRange();
        InvalidateVisual();
    }

    public void ZoomRightYToZeroToPlus3Sd()
    {
        if (!_hasRightAxis)
        {
            return;
        }

        var upper = _series.Where(static s => s.ShowSdLines && s.AxisSide is StabilityAxisSide.Right or StabilityAxisSide.Both).Select(static s => s.Mean + (3d * s.StandardDeviation)).DefaultIfEmpty(0d).Max();
        var pad = Math.Max(1e-6, Math.Abs(upper) * 0.05);
        _rightViewYMin = 0d;
        _rightViewYMax = upper + pad;
        EnsureRightMinimumRange();
        InvalidateVisual();
    }

    public void ZoomRightYToZeroToMaxPlusFivePercent()
    {
        if (!_hasRightAxis)
        {
            return;
        }

        var upper = _series
            .Where(static s => s.AxisSide is StabilityAxisSide.Right or StabilityAxisSide.Both)
            .SelectMany(static s => s.Points)
            .DefaultIfEmpty(new ChartPoint(0, 0))
            .Max(static p => p.Y);
        var pad = Math.Max(1e-6, Math.Abs(upper) * 0.05);
        _rightViewYMin = 0d;
        _rightViewYMax = upper + pad;
        EnsureRightMinimumRange();
        InvalidateVisual();
    }

    public void ZoomRightYToPlusMinus3Sd()
    {
        if (!_hasRightAxis)
        {
            return;
        }

        var upper = _series.Where(static s => s.ShowSdLines && s.AxisSide is StabilityAxisSide.Right or StabilityAxisSide.Both).Select(static s => s.Mean + (3d * s.StandardDeviation)).DefaultIfEmpty(0d).Max();
        var lower = _series.Where(static s => s.ShowSdLines && s.AxisSide is StabilityAxisSide.Right or StabilityAxisSide.Both).Select(static s => s.Mean - (3d * s.StandardDeviation)).DefaultIfEmpty(0d).Min();
        var pad = Math.Max(1e-6, (upper - lower) * 0.05);
        _rightViewYMin = lower - pad;
        _rightViewYMax = upper + pad;
        EnsureRightMinimumRange();
        InvalidateVisual();
    }

    private void DrawSeriesPoints(SKCanvas canvas, SKRect dataRect, SeriesData series)
    {
        var visible = series.Points
            .Where(p => p.X >= Viewport.XMin && p.X <= Viewport.XMax)
            .OrderBy(static p => p.X)
            .ToList();
        if (visible.Count == 0)
        {
            return;
        }

        var radius = Math.Max(1.4f, 2.2f * PlotUiScale);
        for (var i = 0; i < visible.Count; i++)
        {
            var px = ToPixelX(visible[i].X, dataRect);
            var py = ToPixelYResolved(visible[i].Y, dataRect, series.AxisSide);
            if (series.ShowMarker)
            {
                canvas.DrawCircle(px, py, radius, _seriesMarkerPaint);
            }

            if (series.ShowLine && i > 0)
            {
                var prevPx = ToPixelX(visible[i - 1].X, dataRect);
                var prevPy = ToPixelYResolved(visible[i - 1].Y, dataRect, series.AxisSide);
                canvas.DrawLine(prevPx, prevPy, px, py, _seriesLinePaint);
            }
        }
    }

    private static SeriesData CreateAxisProjection(SeriesData source, StabilityAxisSide axisSide)
    {
        return new SeriesData
        {
            Label = source.Label,
            Color = source.Color,
            ShowSdLines = source.ShowSdLines,
            ShowLine = source.ShowLine,
            ShowMarker = source.ShowMarker,
            AxisSide = axisSide,
            Points = source.Points,
            Mean = source.Mean,
            StandardDeviation = source.StandardDeviation
        };
    }

    private void DrawSigmaLines(SKCanvas canvas, SKRect dataRect, SeriesData series, StabilityAxisSide targetAxis)
    {
        DrawHorizontalLine(canvas, dataRect, series.Mean, _meanPaint, targetAxis);
        DrawHorizontalLine(canvas, dataRect, series.Mean + (1d * series.StandardDeviation), _sdPaint, targetAxis);
        DrawHorizontalLine(canvas, dataRect, series.Mean - (1d * series.StandardDeviation), _sdPaint, targetAxis);
        DrawHorizontalLine(canvas, dataRect, series.Mean + (2d * series.StandardDeviation), _sdPaint, targetAxis);
        DrawHorizontalLine(canvas, dataRect, series.Mean - (2d * series.StandardDeviation), _sdPaint, targetAxis);
        DrawHorizontalLine(canvas, dataRect, series.Mean + (3d * series.StandardDeviation), _sdPaint, targetAxis);
        DrawHorizontalLine(canvas, dataRect, series.Mean - (3d * series.StandardDeviation), _sdPaint, targetAxis);
    }

    private void DrawHorizontalLine(SKCanvas canvas, SKRect dataRect, double y, SKPaint paint, StabilityAxisSide axisSide)
    {
        if (axisSide == StabilityAxisSide.Left && (y < Viewport.YMin || y > Viewport.YMax))
        {
            return;
        }

        var py = ToPixelYResolved(y, dataRect, axisSide);
        canvas.DrawLine(dataRect.Left, py, dataRect.Right, py, paint);
    }

    private float ToPixelYResolved(double y, SKRect plotRect, StabilityAxisSide side)
    {
        if (side != StabilityAxisSide.Right || !_hasRightAxis)
        {
            return ToPixelY(y, plotRect);
        }

        var denom = Math.Max(1e-9, _rightViewYMax - _rightViewYMin);
        var t = (y - _rightViewYMin) / denom;
        return (float)(plotRect.Bottom - (t * plotRect.Height));
    }

    private void RecalculateRightAxisRange()
    {
        var leftSeries = _series.Where(static s => s.AxisSide is StabilityAxisSide.Left or StabilityAxisSide.Both).ToList();
        var firstLeftColor = leftSeries
            .Select(s => ParseColor(s.Color, SKColors.SteelBlue.WithAlpha(220)))
            .FirstOrDefault();
        _leftAxisColor = firstLeftColor == default ? new SKColor(120, 120, 120, 220) : firstLeftColor;

        var rightSeries = _series.Where(static s => s.AxisSide is StabilityAxisSide.Right or StabilityAxisSide.Both).ToList();
        var rightPoints = rightSeries.SelectMany(static s => s.Points).ToList();
        _hasRightAxis = rightPoints.Count > 0;
        if (!_hasRightAxis)
        {
            _rightYMin = 0;
            _rightYMax = 1;
            _rightViewYMin = 0;
            _rightViewYMax = 1;
            _rightAxisColor = new SKColor(120, 120, 120, 220);
            return;
        }

        _rightYMin = rightPoints.Min(static p => p.Y);
        _rightYMax = rightPoints.Max(static p => p.Y);
        if (Math.Abs(_rightYMax - _rightYMin) < 1e-9)
        {
            _rightYMin -= 0.5;
            _rightYMax += 0.5;
        }
        _rightViewYMin = _rightYMin;
        _rightViewYMax = _rightYMax;
        EnsureRightMinimumRange();

        var firstColored = rightSeries
            .Select(s => ParseColor(s.Color, SKColors.SteelBlue.WithAlpha(220)))
            .FirstOrDefault();
        _rightAxisColor = firstColored == default ? new SKColor(120, 120, 120, 220) : firstColored;
    }

    private void DrawRightAxis(SKCanvas canvas, SKRect dataRect, SKRect fullPlotRect)
    {
        _rightAxisPaint.Color = _rightAxisColor;
        _rightAxisTextPaint.Color = _rightAxisColor.WithAlpha(230);
        var x = dataRect.Right;
        canvas.DrawLine(x, dataRect.Top, x, dataRect.Bottom, _rightAxisPaint);
        _rightAxisTextPaint.TextSize = Math.Max(9f, (float)(11f * PlotUiScale));
        var tickLength = Math.Max(3f * PlotUiScale, _rightAxisTextPaint.TextSize * 0.35f);
        canvas.DrawLine(x, dataRect.Top, x + tickLength, dataRect.Top, _rightAxisPaint);
        canvas.DrawLine(x, dataRect.Bottom, x + tickLength, dataRect.Bottom, _rightAxisPaint);
        var labelX = Math.Min(fullPlotRect.Right - 2f, x + tickLength + Math.Max(2f * PlotUiScale, RightAxisGapDip * PlotUiScale));
        var ticks = BuildRightAxisTicks(dataRect, out var step);
        var metrics = AxisFont.Metrics;
        var yMinGap = 3f * PlotUiScale;
        var lastLabelTop = float.PositiveInfinity;
        foreach (var tick in ticks)
        {
            if (!NonNegativeAxisTickPolicy.AllowsLabel(tick))
            {
                continue;
            }

            var y = ToPixelYResolved(tick, dataRect, StabilityAxisSide.Right);
            canvas.DrawLine(x, y, x + tickLength, y, _rightAxisPaint);
            var baseline = y - (metrics.Ascent + metrics.Descent) * 0.5f;
            var top = baseline + metrics.Ascent;
            var bottom = baseline + metrics.Descent;
            if (bottom >= lastLabelTop - yMinGap)
            {
                continue;
            }

            var label = AxisTickLabelFormatter.FormatSnappedToGrid(tick, step, AxisLabelPrecisionY, null, AxisLabelFormatY);
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }
            canvas.DrawText(label, labelX, baseline, _rightAxisTextPaint);
            lastLabelTop = top;
        }
    }

    private void EnsureRightMinimumRange()
    {
        if (_rightViewYMax - _rightViewYMin < 1e-9)
        {
            var center = (_rightViewYMax + _rightViewYMin) * 0.5;
            _rightViewYMin = center - 0.5;
            _rightViewYMax = center + 0.5;
        }
    }

    private SKRect GetDataPlotRect(SKRect fullPlotRect)
    {
        if (!_hasRightAxis)
        {
            return fullPlotRect;
        }

        var reserve = ComputeRightAxisReserve(fullPlotRect);
        var right = Math.Max(fullPlotRect.Left + 24f, fullPlotRect.Right - reserve);
        return new SKRect(fullPlotRect.Left, fullPlotRect.Top, right, fullPlotRect.Bottom);
    }

    private float ComputeRightAxisReserve(SKRect fullPlotRect)
    {
        if (!_hasRightAxis)
        {
            return 0f;
        }

        var maxLabel = Math.Max(
            AxisFont.MeasureText(_rightViewYMax.ToString("G4")),
            AxisFont.MeasureText(_rightViewYMin.ToString("G4")));
        var metrics = AxisFont.Metrics;
        var axisFontHeight = Math.Max(1f, -metrics.Ascent + metrics.Descent);
        var tickLength = Math.Max(3f * PlotUiScale, axisFontHeight * 0.35f);
        var gap = Math.Max(RightAxisGapDip * PlotUiScale, axisFontHeight * 0.25f);
        var reserve = tickLength + gap + maxLabel + Math.Max(2f * PlotUiScale, 2f);
        return Math.Min(fullPlotRect.Width * 0.45f, reserve);
    }

    private void DrawLeftAxisColored(SKCanvas canvas, SKRect dataRect)
    {
        var stroke = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = AxisStrokePaint.StrokeWidth,
            Color = _leftAxisColor
        };
        var text = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = _leftAxisColor.WithAlpha(230)
        };

        var zs = PlotUiScale;
        var axOff = Plotto.Charting.Geometry.ChartPlotLayout.AxisLabelInsetFromPlotLeft(zs);
        var axisMetrics = AxisFont.Metrics;
        var axisFontHeight = Math.Max(1f, -axisMetrics.Ascent + axisMetrics.Descent);
        var yTickRight = dataRect.Left - Math.Max(axOff, axisFontHeight * 0.5f);
        var yTickLength = Math.Max(3f * zs, axisFontHeight * 0.35f);
        var yMinGap = 3f * zs;

        canvas.DrawLine(dataRect.Left, dataRect.Top, dataRect.Left, dataRect.Bottom, stroke);

        var maxYTickCount = Math.Clamp((int)(dataRect.Height / Math.Max(10f * zs, axisFontHeight * 0.9f)), 2, 120);
        var bestYTickRequest = 2;
        var yTicks = NiceAxisTickGrid.Generate(Viewport.YMin, Viewport.YMax, bestYTickRequest);
        for (var candidateTickCount = 3; candidateTickCount <= maxYTickCount; candidateTickCount++)
        {
            var candidateTicks = NiceAxisTickGrid.Generate(Viewport.YMin, Viewport.YMax, candidateTickCount, out _);
            var lastTopProbe = float.PositiveInfinity;
            var overlaps = false;
            foreach (var candidateTick in candidateTicks)
            {
                if (!NonNegativeAxisTickPolicy.AllowsLabel(candidateTick))
                {
                    continue;
                }

                var py = ToPixelY(candidateTick, dataRect);
                var baselineProbe = py - (axisMetrics.Ascent + axisMetrics.Descent) * 0.5f;
                var topProbe = baselineProbe + axisMetrics.Ascent;
                var bottomProbe = baselineProbe + axisMetrics.Descent;
                if (bottomProbe >= lastTopProbe - yMinGap)
                {
                    overlaps = true;
                    break;
                }

                lastTopProbe = topProbe;
            }

            if (overlaps)
            {
                break;
            }

            yTicks = candidateTicks;
            bestYTickRequest = candidateTickCount;
        }

        yTicks = NiceAxisTickGrid.Generate(Viewport.YMin, Viewport.YMax, bestYTickRequest, out var yStep);
        var lastLabelTop = float.PositiveInfinity;
        foreach (var tick in yTicks)
        {
            if (!NonNegativeAxisTickPolicy.AllowsLabel(tick))
            {
                continue;
            }

            var y = ToPixelY(tick, dataRect);
            canvas.DrawLine(dataRect.Left - yTickLength, y, dataRect.Left, y, stroke);
            var baseline = y - (axisMetrics.Ascent + axisMetrics.Descent) * 0.5f;
            var top = baseline + axisMetrics.Ascent;
            var bottom = baseline + axisMetrics.Descent;
            if (bottom >= lastLabelTop - yMinGap)
            {
                continue;
            }

            var label = AxisTickLabelFormatter.FormatSnappedToGrid(tick, yStep, AxisLabelPrecisionY, null, AxisLabelFormatY);
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            canvas.DrawText(label, yTickRight, baseline, SKTextAlign.Right, AxisFont, text);
            lastLabelTop = top;
        }
    }

    private static SKColor ParseColor(string colorText, SKColor fallback)
    {
        if (string.IsNullOrWhiteSpace(colorText))
        {
            return fallback;
        }

        if (SKColor.TryParse(colorText, out var parsed))
        {
            return parsed;
        }

        try
        {
            var media = (Color)ColorConverter.ConvertFromString(colorText);
            return new SKColor(media.R, media.G, media.B, media.A);
        }
        catch
        {
            return fallback;
        }
    }

    public override bool ApplyWheelZoomFromRoute(MouseWheelEventArgs e)
    {
        if (e.Handled || !CanInteract())
        {
            return false;
        }

        var pivot = e.GetPosition(this);
        var factor = e.Delta > 0 ? 0.9 : 1.1;
        if (IsPointerOverXAxisZone(pivot))
        {
            ZoomXAxisAroundMouseToCenter(pivot, factor);
            e.Handled = true;
            return true;
        }

        UpdateActiveAxisFromPoint(pivot, preferAxisBands: true);
        if (_activeAxisSide == StabilityAxisSide.Right && _hasRightAxis)
        {
            ZoomRightYAt(pivot, factor);
            e.Handled = true;
            return true;
        }

        ZoomLeftYAt(pivot, factor);
        e.Handled = true;
        return true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        UpdateActiveAxisFromPoint(e.GetPosition(this), preferAxisBands: true);
        base.OnMouseDown(e);
    }

    protected override void OnDoubleRightClickZoomOut(Point logicalPosition)
    {
        UpdateActiveAxisFromPoint(logicalPosition, preferAxisBands: true);
        if (_activeAxisSide == StabilityAxisSide.Right && _hasRightAxis)
        {
            ZoomRightYToZeroToMaxPlusFivePercent();
            return;
        }

        ZoomLeftYToZeroToMaxPlusFivePercent();
    }

    public void HandleExternalRightDoubleClick(Point logicalPosition)
    {
        // When hosted in Griddo, right-clicks can be relayed without native ClickCount=2 on chart events.
        // Temporarily null the context menu so any pending deferred open from the first click is ignored.
        var originalMenu = ContextMenu;
        if (originalMenu is not null)
        {
            ContextMenu = null;
            var restoreTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(550)
            };
            restoreTimer.Tick += (_, _) =>
            {
                restoreTimer.Stop();
                if (ContextMenu is null)
                {
                    ContextMenu = originalMenu;
                }
            };
            restoreTimer.Start();
        }

        OnDoubleRightClickZoomOut(logicalPosition);
    }

    private void UpdateActiveAxisFromPoint(Point pointInControl, bool preferAxisBands = false)
    {
        var dataRect = GetDataPlotRect(PlotRect);
        if (preferAxisBands)
        {
            if (_hasRightAxis && pointInControl.X >= dataRect.Right)
            {
                _activeAxisSide = StabilityAxisSide.Right;
                return;
            }

            if (pointInControl.X <= dataRect.Left)
            {
                _activeAxisSide = StabilityAxisSide.Left;
                return;
            }
        }

        var splitX = dataRect.Left + (dataRect.Width * 0.5f);
        _activeAxisSide = _hasRightAxis && pointInControl.X >= splitX
            ? StabilityAxisSide.Right
            : StabilityAxisSide.Left;
    }

    private void ZoomRightYAt(Point pivot, double scale)
    {
        if (!_hasRightAxis)
        {
            return;
        }

        var leftRange = Math.Max(1e-9, Viewport.YMax - Viewport.YMin);
        var leftPivot = ToChartPoint(pivot).Y;
        var t = (leftPivot - Viewport.YMin) / leftRange;
        t = Math.Clamp(t, 0d, 1d);

        var rightRangeCurrent = Math.Max(1e-9, _rightViewYMax - _rightViewYMin);
        var pivotDataY = _rightViewYMin + (t * rightRangeCurrent);
        var range = rightRangeCurrent * scale;
        _rightViewYMin = pivotDataY - ((pivotDataY - _rightViewYMin) * scale);
        _rightViewYMax = _rightViewYMin + range;
        EnsureRightMinimumRange();
        InvalidateVisual();
    }

    private void ZoomLeftYAt(Point pivot, double scale)
    {
        if (!_hasLeftAxis)
        {
            return;
        }

        var pivotDataY = ToChartPoint(pivot).Y;
        var range = (Viewport.YMax - Viewport.YMin) * scale;
        Viewport.YMin = pivotDataY - ((pivotDataY - Viewport.YMin) * scale);
        Viewport.YMax = Viewport.YMin + range;
        Viewport.EnsureMinimumSize();
        InvalidateVisual();
    }

    private bool IsPointerOverXAxisZone(Point pointInControl)
    {
        if (!ShowXAxis)
        {
            return false;
        }

        var dataRectDip = GetDataPlotRectDip();
        var bandHeight = Math.Max(10d, 22d * PlotUiScale);
        return pointInControl.X >= dataRectDip.Left
               && pointInControl.X <= dataRectDip.Right
               && pointInControl.Y >= dataRectDip.Bottom
               && pointInControl.Y <= dataRectDip.Bottom + bandHeight;
    }

    private void ZoomXAxisAroundMouseToCenter(Point pivot, double scale)
    {
        ZoomXAt(pivot, scale);
    }

    private Rect GetDataPlotRectDip()
    {
        var dataRect = GetDataPlotRect(PlotRect);
        var dpi = VisualTreeHelper.GetDpi(this);
        var sx = Math.Max(1e-9, dpi.DpiScaleX);
        var sy = Math.Max(1e-9, dpi.DpiScaleY);
        return new Rect(
            dataRect.Left / sx,
            dataRect.Top / sy,
            dataRect.Width / sx,
            dataRect.Height / sy);
    }

    private IReadOnlyList<double> BuildRightAxisTicks(SKRect dataRect, out double step)
    {
        step = 0d;
        if (!_hasRightAxis)
        {
            return [];
        }

        var zs = PlotUiScale;
        var axisMetrics = AxisFont.Metrics;
        var axisFontHeight = Math.Max(1f, -axisMetrics.Ascent + axisMetrics.Descent);
        var yMinGap = 3f * zs;
        var maxYTickCount = Math.Clamp((int)(dataRect.Height / Math.Max(10f * zs, axisFontHeight * 0.9f)), 2, 120);
        var bestYTickRequest = 2;
        var yTicks = NiceAxisTickGrid.Generate(_rightViewYMin, _rightViewYMax, bestYTickRequest);
        for (var candidateTickCount = 3; candidateTickCount <= maxYTickCount; candidateTickCount++)
        {
            var candidateTicks = NiceAxisTickGrid.Generate(_rightViewYMin, _rightViewYMax, candidateTickCount, out var candidateStep);
            var lastTopProbe = float.PositiveInfinity;
            var overlaps = false;
            foreach (var candidateTick in candidateTicks)
            {
                if (!NonNegativeAxisTickPolicy.AllowsLabel(candidateTick))
                {
                    continue;
                }

                var py = ToPixelYResolved(candidateTick, dataRect, StabilityAxisSide.Right);
                var baselineProbe = py - (axisMetrics.Ascent + axisMetrics.Descent) * 0.5f;
                var topProbe = baselineProbe + axisMetrics.Ascent;
                var bottomProbe = baselineProbe + axisMetrics.Descent;
                var label = AxisTickLabelFormatter.FormatSnappedToGrid(candidateTick, candidateStep, AxisLabelPrecisionY, null, AxisLabelFormatY);
                if (string.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                if (bottomProbe >= lastTopProbe - yMinGap)
                {
                    overlaps = true;
                    break;
                }

                lastTopProbe = topProbe;
            }

            if (overlaps)
            {
                break;
            }

            yTicks = candidateTicks;
            bestYTickRequest = candidateTickCount;
        }

        yTicks = NiceAxisTickGrid.Generate(_rightViewYMin, _rightViewYMax, bestYTickRequest, out step);
        return yTicks;
    }

    public sealed class SeriesData
    {
        public string Label { get; init; } = string.Empty;
        public string Color { get; init; } = string.Empty;
        public bool ShowSdLines { get; init; }
        public bool ShowLine { get; init; }
        public bool ShowMarker { get; init; } = true;
        public StabilityAxisSide AxisSide { get; init; } = StabilityAxisSide.Left;
        public IReadOnlyList<ChartPoint> Points { get; init; } = [];
        public double Mean { get; init; }
        public double StandardDeviation { get; init; }
    }
}
