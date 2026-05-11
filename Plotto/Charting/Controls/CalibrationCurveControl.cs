using System.Windows;
using System.Windows.Input;
using SkiaSharp;
using Plotto.Charting.Axes;
using Plotto.Charting.Core;
using Plotto.Charting.Geometry;
using Plotto.Charting.Rendering;

namespace Plotto.Charting.Controls;

public class CalibrationCurveControl : SkiaChartBaseControl
{
    private readonly SKPaint _pointEnabledPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = new SKColor(220, 55, 55)
    };

    private readonly SKPaint _pointDisabledPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = new SKColor(160, 45, 45, 180)
    };

    private readonly SKPaint _fitPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 2f,
        Color = new SKColor(65, 135, 225)
    };

    private readonly SKPaint _pointStrokePaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1f,
        Color = new SKColor(140, 30, 30)
    };

    private readonly SKPaint _pointQcPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = new SKColor(55, 145, 240)
    };

    private readonly SKPaint _pointQcStrokePaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1f,
        Color = new SKColor(28, 88, 170)
    };

    private readonly SKPaint _pointCurrentPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = new SKColor(55, 190, 95)
    };

    private readonly SKPaint _pointCurrentStrokePaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1f,
        Color = new SKColor(28, 120, 55)
    };

    private readonly SKPaint _originGuidePaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1f,
        Color = new SKColor(150, 150, 150, 220)
    };

    private readonly SKPaint _currentQuantifierGuidePaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1.5f,
        Color = new SKColor(28, 120, 55, 230)
    };

    private readonly SKPaint _labelConnectorPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 2.25f,
        Color = new SKColor(90, 90, 90, 200)
    };

    private readonly SKPaint _labelTextPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = new SKColor(40, 40, 40)
    };

    private readonly List<(CalibrationPoint Point, SKRect Bounds)> _calibrationLabelHitRegions = [];

    private int _suppressCalibrationViewportFitDepth;

    /// <summary>
    /// While &gt; 0, calibration / overlay changes do not auto-fit the viewport (hosted grid restores saved zoom).
    /// </summary>
    public void BeginSuppressCalibrationViewportFit() => _suppressCalibrationViewportFitDepth++;

    public void EndSuppressCalibrationViewportFit()
    {
        if (_suppressCalibrationViewportFitDepth > 0)
        {
            _suppressCalibrationViewportFitDepth--;
        }
    }

    /// <summary>Fit Y/X bounds to calibration points and curve overlay (used when no saved viewport exists).</summary>
    public void FitViewportToCalibrationData() => FitViewportToCalibrationPoints();

    public bool ShowCalibrationPointLabels
    {
        get => (bool)GetValue(ShowCalibrationPointLabelsProperty);
        set => SetValue(ShowCalibrationPointLabelsProperty, value);
    }

    public static readonly DependencyProperty ShowCalibrationPointLabelsProperty =
        DependencyProperty.Register(
            nameof(ShowCalibrationPointLabels),
            typeof(bool),
            typeof(CalibrationCurveControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Logical px used for Skia label measurement (scaled by <see cref="SkiaChartBaseControl.UiScale"/>).</summary>
    public double CalibrationPointLabelFontSize
    {
        get => (double)GetValue(CalibrationPointLabelFontSizeProperty);
        set => SetValue(CalibrationPointLabelFontSizeProperty, value);
    }

    public static readonly DependencyProperty CalibrationPointLabelFontSizeProperty =
        DependencyProperty.Register(
            nameof(CalibrationPointLabelFontSize),
            typeof(double),
            typeof(CalibrationCurveControl),
            new FrameworkPropertyMetadata(9d, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<CalibrationPoint> CalibrationPoints
    {
        get => (IReadOnlyList<CalibrationPoint>)GetValue(CalibrationPointsProperty);
        set => SetValue(CalibrationPointsProperty, value);
    }

    public static readonly DependencyProperty CalibrationPointsProperty =
        DependencyProperty.Register(
            nameof(CalibrationPoints),
            typeof(IReadOnlyList<CalibrationPoint>),
            typeof(CalibrationCurveControl),
            new FrameworkPropertyMetadata(
                Array.Empty<CalibrationPoint>(),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnCalibrationChanged,
                CoerceCalibrationPoints));

    private static object CoerceCalibrationPoints(DependencyObject d, object baseValue)
    {
        return baseValue ?? Array.Empty<CalibrationPoint>();
    }

    /// <summary>Kept for host binding; the fitted line comes from <see cref="CurveOverlayPoints"/> when set.</summary>
    public CalibrationFitMode FitMode
    {
        get => (CalibrationFitMode)GetValue(FitModeProperty);
        set => SetValue(FitModeProperty, value);
    }

    public static readonly DependencyProperty FitModeProperty =
        DependencyProperty.Register(
            nameof(FitMode),
            typeof(CalibrationFitMode),
            typeof(CalibrationCurveControl),
            new FrameworkPropertyMetadata(CalibrationFitMode.Linear, FrameworkPropertyMetadataOptions.AffectsRender, OnFitModeChanged));

    /// <summary>Optional fitted curve in plot coordinates (e.g. from Quanto bracket); when set, this line is drawn instead of a chord through points.</summary>
    public IReadOnlyList<ChartPoint>? CurveOverlayPoints
    {
        get => (IReadOnlyList<ChartPoint>?)GetValue(CurveOverlayPointsProperty);
        set => SetValue(CurveOverlayPointsProperty, value);
    }

    public static readonly DependencyProperty CurveOverlayPointsProperty =
        DependencyProperty.Register(
            nameof(CurveOverlayPoints),
            typeof(IReadOnlyList<ChartPoint>),
            typeof(CalibrationCurveControl),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnCurveOverlayChanged));

    public double CurrentQuantifierGuideX
    {
        get => (double)GetValue(CurrentQuantifierGuideXProperty);
        set => SetValue(CurrentQuantifierGuideXProperty, value);
    }

    public static readonly DependencyProperty CurrentQuantifierGuideXProperty =
        DependencyProperty.Register(
            nameof(CurrentQuantifierGuideX),
            typeof(double),
            typeof(CalibrationCurveControl),
            new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public double CurrentQuantifierGuideY
    {
        get => (double)GetValue(CurrentQuantifierGuideYProperty);
        set => SetValue(CurrentQuantifierGuideYProperty, value);
    }

    public static readonly DependencyProperty CurrentQuantifierGuideYProperty =
        DependencyProperty.Register(
            nameof(CurrentQuantifierGuideY),
            typeof(double),
            typeof(CalibrationCurveControl),
            new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public event EventHandler<CalibrationPointEventArgs>? CalibrationPointToggled;

    /// <summary>
    /// Allow point/label toggle while <see cref="SkiaChartBaseControl.RenderMode"/> is Renderer so grid-hosted plots
    /// behave like chromatograms (first click activates via relay; direct clicks still hit the chart).
    /// Wheel zoom stays editor-only via <see cref="SkiaChartBaseControl.CanUseScrollWheelZoom"/>.
    /// </summary>
    protected override bool CanInteract() =>
        EnableMouseInteractions && EnableInlineEditing;

    /// <summary>
    /// Wheel zoom on X by default; Ctrl+wheel zooms Y (see <see cref="ApplyWheelZoomFromRoute"/>).
    /// </summary>
    protected override void ApplyUiScaleToResources()
    {
        base.ApplyUiScaleToResources();
        var s = PlotUiScale;
        _fitPaint.StrokeWidth = 2f * s;
        _pointStrokePaint.StrokeWidth = Math.Max(0.5f, 1f * s);
        _originGuidePaint.StrokeWidth = Math.Max(0.5f, 1f * s);
        _currentQuantifierGuidePaint.StrokeWidth = Math.Max(0.75f, 1.5f * s);
        _currentQuantifierGuidePaint.PathEffect = SKPathEffect.CreateDash(new[] { 6f * s, 4f * s }, 0f);
        _labelConnectorPaint.StrokeWidth = Math.Max(1f, 2.25f * s);
    }

    public override bool ApplyWheelZoomFromRoute(MouseWheelEventArgs e)
    {
        if (!CanUseScrollWheelZoom())
        {
            return false;
        }

        var pivot = e.GetPosition(this);
        var factor = e.Delta > 0 ? 0.9 : 1.1;
        var mod = Keyboard.Modifiers;
        var ctrlWithoutShift = mod.HasFlag(ModifierKeys.Control) && !mod.HasFlag(ModifierKeys.Shift);
        if (ctrlWithoutShift)
        {
            ZoomYAt(pivot, factor);
        }
        else
        {
            ZoomXAt(pivot, factor);
        }

        return true;
    }

    protected override void OnChartMouseUp(ChartPoint point, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || CalibrationPoints.Count == 0)
        {
            return;
        }

        var surf = MapMouseEventToSurfacePixels(e);
        var sx = (float)surf.X;
        var sy = (float)surf.Y;
        foreach (var (pt, bounds) in _calibrationLabelHitRegions)
        {
            if (bounds.Contains(sx, sy))
            {
                ToggleCalibrationPoint(pt);
                return;
            }
        }

        var nearest = FindNearest(point, 0.045);
        if (nearest is null)
        {
            return;
        }

        ToggleCalibrationPoint(nearest);
    }

    private void ToggleCalibrationPoint(CalibrationPoint nearest)
    {
        if (!nearest.AllowEnabledToggle)
        {
            return;
        }

        nearest.IsEnabled = !nearest.IsEnabled;
        CalibrationPointToggled?.Invoke(this, new CalibrationPointEventArgs(nearest));
        RequestRender();
    }

    private (SKPaint Fill, SKPaint Stroke) ResolveCalibrationMarkerPaints(CalibrationPoint point) =>
        point.PointKind switch
        {
            CalibrationPlotPointKind.QualityControl => ( _pointQcPaint, _pointQcStrokePaint ),
            CalibrationPlotPointKind.CurrentSample => ( _pointCurrentPaint, _pointCurrentStrokePaint ),
            _ => (
                point.IsEnabled ? _pointEnabledPaint : _pointDisabledPaint,
                _pointStrokePaint),
        };

    /// <summary>Draws <see cref="CurveOverlayPoints"/> when provided; otherwise a polyline through enabled calibration points.</summary>
    protected override void DrawSeries(SKCanvas canvas, IReadOnlyList<ChartPoint> points, SKRect plotRect)
    {
        _ = points;
        IReadOnlyList<ChartPoint> line = CurveOverlayPoints is { Count: >= 2 } overlay
            ? overlay
            : CalibrationPoints
                .Where(static p => p.IsEnabled && p.PointKind == CalibrationPlotPointKind.CalibrationStandard)
                .OrderBy(static p => p.X)
                .Select(static p => new ChartPoint(p.X, p.Y))
                .ToArray();
        if (line.Count < 2)
        {
            return;
        }

        ChartSkiaLineSeries.DrawPolyline(canvas, line, plotRect, ToPixelX, ToPixelY, _fitPaint);
    }

    protected override void DrawAxes(SKCanvas canvas, SKRect plotRect)
    {
        if (UseSparklineLayout || (!ShowXAxis && !ShowYAxis))
        {
            return;
        }

        const double eps = 1e-12;
        // x=0 lies left of the plot: show y-axis spine. y=0 lies below the plot bottom: show x-axis spine.
        var showYAxisLine = ShowYAxis && Viewport.XMin > eps;
        var showXAxisLine = ShowXAxis && Viewport.YMin > eps;
        if (showYAxisLine)
        {
            canvas.DrawLine(plotRect.Left, plotRect.Top, plotRect.Left, plotRect.Bottom, AxisStrokePaint);
        }

        if (showXAxisLine)
        {
            canvas.DrawLine(plotRect.Left, plotRect.Bottom, plotRect.Right, plotRect.Bottom, AxisStrokePaint);
        }

        var zs = PlotUiScale;
        var axOff = ChartPlotLayout.AxisLabelInsetFromPlotLeft(zs);
        var axisMetrics = AxisFont.Metrics;
        var axisFontHeight = Math.Max(1f, -axisMetrics.Ascent + axisMetrics.Descent);
        var xTickTop = plotRect.Bottom + (axisFontHeight * 0.2f);
        var xTickBaseline = xTickTop - axisMetrics.Ascent;
        var xTickLength = Math.Max(3f * zs, axisFontHeight * 0.35f);
        var xAxisReserveY = ShowXAxis
            ? ChartPlotLayout.ComputeXAxisReserveY(zs, AxisFontSize, !string.IsNullOrWhiteSpace(AxisLabelX))
            : 0f;
        var yTickRight = plotRect.Left - Math.Max(axOff, axisFontHeight * 0.5f);
        var yTickLength = Math.Max(3f * zs, axisFontHeight * 0.35f);

        if (ShowXAxis)
        {
            var minGap = 0f;
            var maxXTickCount = Math.Clamp((int)(plotRect.Width / Math.Max(10f * zs, axisFontHeight * 0.9f)), 2, 120);
            var bestXTickRequest = 2;
            var xTicks = NiceAxisTickGrid.Generate(Viewport.XMin, Viewport.XMax, bestXTickRequest);
            for (var candidateTickCount = 3; candidateTickCount <= maxXTickCount; candidateTickCount++)
            {
                var candidateTicks = NiceAxisTickGrid.Generate(Viewport.XMin, Viewport.XMax, candidateTickCount, out var candStep);
                var lastRight = float.NegativeInfinity;
                var overlaps = false;
                foreach (var candidateTick in candidateTicks)
                {
                    if (!NonNegativeAxisTickPolicy.AllowsLabel(candidateTick))
                    {
                        continue;
                    }

                    var x = ToPixelX(candidateTick, plotRect);
                    var label = AxisTickLabelFormatter.FormatSnappedToGrid(candidateTick, candStep, AxisLabelPrecisionX, null, AxisLabelFormatX);
                    var width = AxisFont.MeasureText(label);
                    var left = x - (width * 0.5f);
                    left = Math.Clamp(left, plotRect.Left, plotRect.Right - width);
                    if (left < lastRight + minGap)
                    {
                        overlaps = true;
                        break;
                    }

                    lastRight = left + width;
                }

                if (overlaps)
                {
                    break;
                }

                xTicks = candidateTicks;
                bestXTickRequest = candidateTickCount;
            }

            xTicks = NiceAxisTickGrid.Generate(Viewport.XMin, Viewport.XMax, bestXTickRequest, out var xStep);
            var lastLabelRight = float.NegativeInfinity;
            foreach (var tick in xTicks)
            {
                if (!NonNegativeAxisTickPolicy.AllowsLabel(tick))
                {
                    continue;
                }

                var x = ToPixelX(tick, plotRect);
                canvas.DrawLine(x, plotRect.Bottom, x, plotRect.Bottom + xTickLength, AxisStrokePaint);
                var label = AxisTickLabelFormatter.FormatSnappedToGrid(tick, xStep, AxisLabelPrecisionX, null, AxisLabelFormatX);
                var width = AxisFont.MeasureText(label);
                var left = x - (width * 0.5f);
                left = Math.Clamp(left, plotRect.Left, plotRect.Right - width);
                var right = left + width;
                if (left < lastLabelRight + minGap)
                {
                    continue;
                }

                canvas.DrawText(label, left, xTickBaseline, SKTextAlign.Left, AxisFont, AxisLabelPaint);
                lastLabelRight = right;
            }
        }

        double yLabelScale = 1;
        var yPowExponent = 0;
        var useYPowerOfTen = false;

        if (ShowYAxis)
        {
            useYPowerOfTen = YAxisPowerOfTenFormatting.TryGetScale(Viewport.YMin, Viewport.YMax, out yLabelScale, out yPowExponent);
            var yMinGap = 3f * zs;
            var maxYTickCount = Math.Clamp((int)(plotRect.Height / Math.Max(10f * zs, axisFontHeight * 0.9f)), 2, 120);
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

                    var py = ToPixelY(candidateTick, plotRect);
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
            SKRect yExponentBadgePadded = default;
            var hasYExponentBadge = false;
            if (useYPowerOfTen)
            {
                var bb = YAxisPowerOfTenFormatting.GetExponentBadgeBounds(plotRect, zs, AxisFont, yPowExponent);
                var pad = 2f * zs;
                yExponentBadgePadded = SKRect.Create(
                    bb.Left - pad,
                    bb.Top - pad,
                    bb.Width + 2f * pad,
                    bb.Height + 2f * pad);
                hasYExponentBadge = true;
            }

            var lastLabelTop = float.PositiveInfinity;
            foreach (var tick in yTicks)
            {
                if (!NonNegativeAxisTickPolicy.AllowsLabel(tick))
                {
                    continue;
                }

                var y = ToPixelY(tick, plotRect);
                canvas.DrawLine(plotRect.Left - yTickLength, y, plotRect.Left, y, AxisStrokePaint);
                var baseline = y - (axisMetrics.Ascent + axisMetrics.Descent) * 0.5f;
                var top = baseline + axisMetrics.Ascent;
                var bottom = baseline + axisMetrics.Descent;
                if (bottom >= lastLabelTop - yMinGap)
                {
                    continue;
                }

                var label = useYPowerOfTen
                    ? AxisTickLabelFormatter.FormatSnappedToGrid(
                        tick / yLabelScale,
                        yStep / yLabelScale,
                        5,
                        null,
                        AxisLabelFormatY)
                    : AxisTickLabelFormatter.FormatSnappedToGrid(tick, yStep, AxisLabelPrecisionY, null, AxisLabelFormatY);
                if (hasYExponentBadge)
                {
                    var lw = AxisFont.MeasureText(label);
                    var lblTop = baseline + axisMetrics.Ascent;
                    var lblH = Math.Max(1e-3f, axisMetrics.Descent - axisMetrics.Ascent);
                    var lblRect = SKRect.Create(yTickRight - lw, lblTop, lw, lblH);
                    if (lblRect.IntersectsWith(yExponentBadgePadded))
                    {
                        continue;
                    }
                }

                canvas.DrawText(label, yTickRight, baseline, SKTextAlign.Right, AxisFont, AxisLabelPaint);
                lastLabelTop = top;
            }

            if (useYPowerOfTen)
            {
                YAxisPowerOfTenFormatting.DrawYAxisExponentBadge(canvas, plotRect, zs, AxisFont, AxisLabelPaint, yPowExponent);
            }
        }

        if (ShowXAxis && !string.IsNullOrWhiteSpace(AxisLabelX))
        {
            var cellBottom = plotRect.Bottom + xAxisReserveY;
            var titleBottom = cellBottom - (2f * zs) + (axisFontHeight * 0.3f);
            var xTitleBaseline = titleBottom - axisMetrics.Descent;
            canvas.DrawText(AxisLabelX, (plotRect.Left + plotRect.Right) / 2f, xTitleBaseline, SKTextAlign.Center, AxisFont, AxisLabelPaint);
        }

        if (ShowYAxis && !string.IsNullOrWhiteSpace(AxisLabelY))
        {
            var yCaption = AxisLabelY;
            var yTitleLeftInset = (ChartPlotLayout.CellPadding * zs) + (2f * zs);
            var yTitleCenterX = yTitleLeftInset + Math.Max(0f, -axisMetrics.Ascent) - (axisFontHeight * 0.5f);
            var titleCenterY = (plotRect.Top + plotRect.Bottom) * 0.5f;
            var drawYAxisCaption = true;
            if (useYPowerOfTen)
            {
                var bb = YAxisPowerOfTenFormatting.GetExponentBadgeBounds(plotRect, zs, AxisFont, yPowExponent);
                var pad = 2f * zs;
                var badgePadded = SKRect.Create(
                    bb.Left - pad,
                    bb.Top - pad,
                    bb.Width + 2f * pad,
                    bb.Height + 2f * pad);
                var textW = AxisFont.MeasureText(yCaption);
                var halfW = textW * 0.5f;
                var halfH = axisFontHeight * 0.5f;

                bool RotatedCaptionOverlapsBadge(float cy)
                {
                    var ax0 = yTitleCenterX - halfH;
                    var ay0 = cy - halfW;
                    var titleBounds = SKRect.Create(ax0, ay0, axisFontHeight, textW);
                    return titleBounds.IntersectsWith(badgePadded);
                }

                if (RotatedCaptionOverlapsBadge(titleCenterY))
                {
                    var step = Math.Max(4f * zs, axisFontHeight * 0.35f);
                    var limit = plotRect.Bottom + halfW + 6f * zs;
                    while (RotatedCaptionOverlapsBadge(titleCenterY) && titleCenterY < limit)
                    {
                        titleCenterY += step;
                    }

                    if (RotatedCaptionOverlapsBadge(titleCenterY))
                    {
                        drawYAxisCaption = false;
                    }
                }
            }

            if (drawYAxisCaption)
            {
                canvas.Save();
                canvas.Translate(yTitleCenterX, titleCenterY);
                canvas.RotateDegrees(-90f);
                canvas.DrawText(yCaption, 0f, 0f, SKTextAlign.Center, AxisFont, AxisLabelPaint);
                canvas.Restore();
            }
        }
    }

    protected override void DrawOverlay(SKCanvas canvas, SKRect plotRect)
    {
        DrawOriginGuideLines(canvas, plotRect);
        DrawCurrentQuantifierGuideLines(canvas, plotRect);

        _calibrationLabelHitRegions.Clear();

        var pr = 10f * PlotUiScale;
        foreach (var point in CalibrationPoints)
        {
            if (point.PointKind == CalibrationPlotPointKind.CurrentSample)
            {
                continue;
            }

            var px = ToPixelX(point.X, plotRect);
            var py = ToPixelY(point.Y, plotRect);
            var (fill, stroke) = ResolveCalibrationMarkerPaints(point);
            canvas.DrawCircle(px, py, pr, fill);
            canvas.DrawCircle(px, py, pr, stroke);
        }

        if (ShowCalibrationPointLabels)
        {
            DrawCalibrationPointLabels(canvas, plotRect, pr);
        }
    }

    private void DrawCalibrationPointLabels(SKCanvas canvas, SKRect plotRect, float markerRadiusPx)
    {
        if (plotRect.Width <= 1f || plotRect.Height <= 1f)
        {
            return;
        }

        var fontPx = (float)Math.Clamp(CalibrationPointLabelFontSize, 6d, 24d) * PlotUiScale;
        using var typeface = SKTypeface.FromFamilyName(null);
        using var font = new SKFont(typeface, fontPx);
        var lineHeight = fontPx * 1.25f;
        var pad = 4f * PlotUiScale;

        var curvePx = BuildCurvePolylinePixels(plotRect);
        var occupied = new List<SKRect>();

        var indices = Enumerable.Range(0, CalibrationPoints.Count).OrderByDescending(i => CalibrationPoints[i].Y).ToList();
        foreach (var i in indices)
        {
            var point = CalibrationPoints[i];
            var text = point.LabelPlainText?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var ax = ToPixelX(point.X, plotRect);
            var ay = ToPixelY(point.Y, plotRect);

            float blockW = 0f;
            foreach (var line in lines)
            {
                blockW = Math.Max(blockW, font.MeasureText(line));
            }

            var blockH = lines.Length * lineHeight;
            var labelStandoff = markerRadiusPx + 40f * PlotUiScale;
            var markerClearance = markerRadiusPx + 10f * PlotUiScale;

            SKRect? placed = null;
            for (var attempt = 0; attempt < 96 && placed is null; attempt++)
            {
                var angle = attempt * (MathF.PI / 12f);
                var radius = labelStandoff + attempt * (12f * PlotUiScale);
                var ox = MathF.Cos(angle) * radius;
                var oy = MathF.Sin(angle) * radius;
                var left = ax + ox - blockW * 0.5f - pad;
                var top = ay + oy - blockH * 0.5f - pad;
                var rect = SKRect.Create(left, top, blockW + 2f * pad, blockH + 2f * pad);
                rect = ClampRectToPlot(rect, plotRect, pad);
                rect = NudgeLabelRectOutsideMarker(rect, ax, ay, markerClearance, plotRect, pad);
                if (IntersectsOccupied(rect, occupied, pad)
                    || RectTooCloseToPolyline(rect, curvePx)
                    || LabelRectIntrudesMarker(rect, ax, ay, markerClearance))
                {
                    continue;
                }

                placed = rect;
            }

            if (placed is null)
            {
                var wBox = blockW + 2f * pad;
                var hBox = blockH + 2f * pad;
                ReadOnlySpan<SKRect> rawCandidates =
                [
                    SKRect.Create(ax + labelStandoff, ay - blockH * 0.5f - pad, wBox, hBox),
                    SKRect.Create(ax - labelStandoff - wBox, ay - blockH * 0.5f - pad, wBox, hBox),
                    SKRect.Create(ax - wBox * 0.5f, ay - labelStandoff - hBox, wBox, hBox),
                    SKRect.Create(ax - wBox * 0.5f, ay + labelStandoff, wBox, hBox),
                ];

                foreach (var raw in rawCandidates)
                {
                    var rect = ClampRectToPlot(raw, plotRect, pad);
                    rect = NudgeLabelRectOutsideMarker(rect, ax, ay, markerClearance, plotRect, pad);
                    if (IntersectsOccupied(rect, occupied, pad)
                        || RectTooCloseToPolyline(rect, curvePx)
                        || LabelRectIntrudesMarker(rect, ax, ay, markerClearance))
                    {
                        continue;
                    }

                    placed = rect;
                    break;
                }

                if (placed is null)
                {
                    var rect = ClampRectToPlot(rawCandidates[0], plotRect, pad);
                    rect = NudgeLabelRectOutsideMarker(rect, ax, ay, markerClearance, plotRect, pad);
                    placed = rect;
                }
            }

            var r = placed.Value;
            occupied.Add(r);

            var start = PointOnCircleToward(ax, ay, markerRadiusPx, r.MidX, r.MidY);
            var end = ClosestPointOnRectBorder(r, start);
            canvas.DrawLine(start, end, _labelConnectorPaint);

            var textLeft = r.Left + pad;
            var baseline = r.Top + pad + font.Size * 0.75f;
            foreach (var line in lines)
            {
                canvas.DrawText(line, textLeft, baseline, SKTextAlign.Left, font, _labelTextPaint);
                baseline += lineHeight;
            }

            var hitPad = 3f * PlotUiScale;
            _calibrationLabelHitRegions.Add((point, OutsetRect(r, hitPad, hitPad)));
        }
    }

    private static SKRect OutsetRect(SKRect r, float dx, float dy) =>
        SKRect.Create(r.Left - dx, r.Top - dy, r.Width + 2f * dx, r.Height + 2f * dy);

    /// <summary>True when the marker center lies inside an expanded "keep-out" disc overlapping the label rect.</summary>
    private static bool LabelRectIntrudesMarker(SKRect rect, float ax, float ay, float forbiddenRadiusPx)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return false;
        }

        var qx = Math.Clamp(ax, rect.Left, rect.Right);
        var qy = Math.Clamp(ay, rect.Top, rect.Bottom);
        var dx = ax - qx;
        var dy = ay - qy;
        return dx * dx + dy * dy < forbiddenRadiusPx * forbiddenRadiusPx;
    }

    private SKRect NudgeLabelRectOutsideMarker(SKRect rect, float ax, float ay, float forbiddenRadiusPx, SKRect plotRect, float margin)
    {
        var step = 8f * PlotUiScale;
        for (var i = 0; i < 36 && LabelRectIntrudesMarker(rect, ax, ay, forbiddenRadiusPx); i++)
        {
            var mx = rect.MidX;
            var my = rect.MidY;
            var rdx = mx - ax;
            var rdy = my - ay;
            var len = Math.Sqrt(rdx * rdx + rdy * rdy);
            if (len < 1e-3)
            {
                rdx = 1f;
                rdy = 0f;
                len = 1;
            }

            var dx = (float)(rdx / len * step);
            var dy = (float)(rdy / len * step);
            rect = SKRect.Create(rect.Left + dx, rect.Top + dy, rect.Width, rect.Height);
            rect = ClampRectToPlot(rect, plotRect, margin);
        }

        return rect;
    }

    private List<SKPoint> BuildCurvePolylinePixels(SKRect plotRect)
    {
        var line = CurveOverlayPoints is { Count: >= 2 } overlay
            ? overlay
            : CalibrationPoints
                .Where(static p => p.IsEnabled && p.PointKind == CalibrationPlotPointKind.CalibrationStandard)
                .OrderBy(static p => p.X)
                .Select(static p => new ChartPoint(p.X, p.Y))
                .ToArray();
        if (line.Count < 2)
        {
            return [];
        }

        List<SKPoint> pts = new(line.Count);
        foreach (var p in line)
        {
            pts.Add(new SKPoint(ToPixelX(p.X, plotRect), ToPixelY(p.Y, plotRect)));
        }

        return pts;
    }

    private static SKRect ClampRectToPlot(SKRect rect, SKRect plotRect, float margin)
    {
        if (plotRect.Width <= 0 || plotRect.Height <= 0)
        {
            return rect;
        }

        var minX = plotRect.Left + margin;
        var maxX = plotRect.Right - margin;
        var minY = plotRect.Top + margin;
        var maxY = plotRect.Bottom - margin;
        if (maxX <= minX || maxY <= minY)
        {
            return SKRect.Create(plotRect.MidX, plotRect.MidY, 0, 0);
        }

        var w = Math.Min(rect.Width, maxX - minX);
        var h = Math.Min(rect.Height, maxY - minY);
        var maxLeft = maxX - w;
        var maxTop = maxY - h;
        var left = maxLeft < minX ? minX : Math.Clamp(rect.Left, minX, maxLeft);
        var top = maxTop < minY ? minY : Math.Clamp(rect.Top, minY, maxTop);
        return SKRect.Create(left, top, w, h);
    }

    private static bool IntersectsOccupied(SKRect rect, List<SKRect> occupied, float pad)
    {
        var inflated = OutsetRect(rect, pad, pad);
        foreach (var o in occupied)
        {
            if (inflated.IntersectsWith(o))
            {
                return true;
            }
        }

        return false;
    }

    private static bool RectTooCloseToPolyline(SKRect rect, IReadOnlyList<SKPoint> poly)
    {
        if (poly.Count < 2)
        {
            return false;
        }

        var cx = rect.MidX;
        var cy = rect.MidY;
        var diag = (float)(0.5 * Math.Sqrt(rect.Width * rect.Width + rect.Height * rect.Height));
        var margin = 6f;
        for (var i = 0; i < poly.Count - 1; i++)
        {
            var d = DistancePointToSegment(cx, cy, poly[i], poly[i + 1]);
            if (d < margin + diag)
            {
                return true;
            }
        }

        return false;
    }

    private static float DistancePointToSegment(float px, float py, SKPoint a, SKPoint b)
    {
        var vx = b.X - a.X;
        var vy = b.Y - a.Y;
        var wx = px - a.X;
        var wy = py - a.Y;
        var c1 = wx * vx + wy * vy;
        if (c1 <= 0)
        {
            return (float)Math.Sqrt(wx * wx + wy * wy);
        }

        var c2 = vx * vx + vy * vy;
        if (c2 <= c1)
        {
            var dx = px - b.X;
            var dy = py - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        var t = c1 / c2;
        var projX = a.X + t * vx;
        var projY = a.Y + t * vy;
        var qx = px - projX;
        var qy = py - projY;
        return (float)Math.Sqrt(qx * qx + qy * qy);
    }

    private static SKPoint PointOnCircleToward(float cx, float cy, float r, float tx, float ty)
    {
        var dx = tx - cx;
        var dy = ty - cy;
        var len = (float)Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-3f)
        {
            return new SKPoint(cx + r, cy);
        }

        return new SKPoint(cx + r * (dx / len), cy + r * (dy / len));
    }

    private static SKPoint ClosestPointOnRectBorder(SKRect r, SKPoint from)
    {
        var cx = Math.Clamp(from.X, r.Left, r.Right);
        var cy = Math.Clamp(from.Y, r.Top, r.Bottom);
        if (from.X < r.Left || from.X > r.Right || from.Y < r.Top || from.Y > r.Bottom)
        {
            return new SKPoint(cx, cy);
        }

        var dl = from.X - r.Left;
        var dr = r.Right - from.X;
        var dt = from.Y - r.Top;
        var db = r.Bottom - from.Y;
        var m = Math.Min(Math.Min(dl, dr), Math.Min(dt, db));
        if (m == dl)
        {
            return new SKPoint(r.Left, cy);
        }

        if (m == dr)
        {
            return new SKPoint(r.Right, cy);
        }

        return m == dt ? new SKPoint(cx, r.Top) : new SKPoint(cx, r.Bottom);
    }

    /// <summary>
    /// Full-height line at x=0 and full-width line at y=0 across the plot when visible.
    /// </summary>
    private void DrawCurrentQuantifierGuideLines(SKCanvas canvas, SKRect plotRect)
    {
        var guideX = CurrentQuantifierGuideX;
        var guideY = CurrentQuantifierGuideY;
        if (!double.IsFinite(guideX) || !double.IsFinite(guideY))
        {
            return;
        }

        var curve = GetCurveChartPoints();
        if (curve.Count < 2)
        {
            return;
        }

        if (TryInterpolateCurveYAtX(curve, guideX, out var curveYAtGuideX))
        {
            var px = ToPixelX(guideX, plotRect);
            var pyAxis = plotRect.Bottom;
            var pyCurve = ToPixelY(curveYAtGuideX, plotRect);
            canvas.DrawLine(px, pyAxis, px, pyCurve, _currentQuantifierGuidePaint);
        }

        if (TryInterpolateCurveXAtY(curve, guideY, guideX, out var curveXAtGuideY))
        {
            var pxAxis = plotRect.Left;
            var pxCurve = ToPixelX(curveXAtGuideY, plotRect);
            var py = ToPixelY(guideY, plotRect);
            canvas.DrawLine(pxAxis, py, pxCurve, py, _currentQuantifierGuidePaint);
        }
    }

    private IReadOnlyList<ChartPoint> GetCurveChartPoints()
    {
        if (CurveOverlayPoints is { Count: >= 2 } overlay)
        {
            return overlay;
        }

        return CalibrationPoints
            .Where(static p => p.IsEnabled && p.PointKind == CalibrationPlotPointKind.CalibrationStandard)
            .OrderBy(static p => p.X)
            .Select(static p => new ChartPoint(p.X, p.Y))
            .ToArray();
    }

    private static bool TryInterpolateCurveYAtX(IReadOnlyList<ChartPoint> curve, double x, out double y)
    {
        y = 0;
        for (var i = 0; i < curve.Count - 1; i++)
        {
            var a = curve[i];
            var b = curve[i + 1];
            var minX = Math.Min(a.X, b.X);
            var maxX = Math.Max(a.X, b.X);
            if (x < minX - 1e-12 || x > maxX + 1e-12)
            {
                continue;
            }

            if (Math.Abs(b.X - a.X) <= 1e-12)
            {
                y = (a.Y + b.Y) * 0.5;
                return true;
            }

            var t = (x - a.X) / (b.X - a.X);
            y = a.Y + t * (b.Y - a.Y);
            return true;
        }

        return false;
    }

    private static bool TryInterpolateCurveXAtY(
        IReadOnlyList<ChartPoint> curve,
        double y,
        double preferredX,
        out double x)
    {
        x = double.NaN;
        var found = false;
        var bestDistance = double.PositiveInfinity;
        for (var i = 0; i < curve.Count - 1; i++)
        {
            var a = curve[i];
            var b = curve[i + 1];
            var minY = Math.Min(a.Y, b.Y);
            var maxY = Math.Max(a.Y, b.Y);
            if (y < minY - 1e-12 || y > maxY + 1e-12)
            {
                continue;
            }

            double candidateX;
            if (Math.Abs(b.Y - a.Y) <= 1e-12)
            {
                candidateX = (a.X + b.X) * 0.5;
            }
            else
            {
                var t = (y - a.Y) / (b.Y - a.Y);
                candidateX = a.X + t * (b.X - a.X);
            }

            var distance = Math.Abs(candidateX - preferredX);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                x = candidateX;
                found = true;
            }
        }

        return found;
    }

    private void DrawOriginGuideLines(SKCanvas canvas, SKRect plotRect)
    {
        if (CalibrationPoints.Count == 0)
        {
            return;
        }

        const double o = 0d;
        if (Viewport is { XMin: <= o, XMax: >= o })
        {
            var px = ToPixelX(o, plotRect);
            var pyBottom = ToPixelY(Viewport.YMin, plotRect);
            var pyTop = ToPixelY(Viewport.YMax, plotRect);
            canvas.DrawLine(px, pyBottom, px, pyTop, _originGuidePaint);
        }

        if (Viewport is { YMin: <= o, YMax: >= o })
        {
            var pxLeft = ToPixelX(Viewport.XMin, plotRect);
            var pxRight = ToPixelX(Viewport.XMax, plotRect);
            var py = ToPixelY(o, plotRect);
            canvas.DrawLine(pxLeft, py, pxRight, py, _originGuidePaint);
        }
    }

    private static void OnCalibrationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (CalibrationCurveControl)d;
        c.SyncChartPointsFromCalibration();
        if (c._suppressCalibrationViewportFitDepth == 0)
        {
            c.FitViewportToCalibrationPoints();
        }

        c.RequestRender();
    }

    private static void OnFitModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        _ = e;
        var c = (CalibrationCurveControl)d;
        c.RequestRender();
    }

    private static void OnCurveOverlayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        _ = e;
        var c = (CalibrationCurveControl)d;
        if (c._suppressCalibrationViewportFitDepth == 0)
        {
            c.FitViewportToCalibrationPoints();
        }

        c.RequestRender();
    }

    private void SyncChartPointsFromCalibration()
    {
        var pts = CalibrationPoints;
        if (pts.Count == 0)
        {
            Points = Array.Empty<ChartPoint>();
            return;
        }

        Points = pts.OrderBy(static p => p.X).Select(static p => new ChartPoint(p.X, p.Y)).ToArray();
    }

    private void FitViewportToCalibrationPoints()
    {
        if (!CalibrationViewportBounds.TryGetZoomOutExtents(CalibrationPoints, CurveOverlayPoints, out var xMin, out var xMax, out var yMin, out var yMax))
        {
            Viewport.XMin = 0;
            Viewport.XMax = 1;
            Viewport.YMin = 0;
            Viewport.YMax = 1;
            Viewport.EnsureMinimumSize();
            return;
        }

        Viewport.XMin = xMin;
        Viewport.YMin = yMin;
        Viewport.XMax = xMax;
        Viewport.YMax = yMax;
        Viewport.EnsureMinimumSize();
    }

    private CalibrationPoint? FindNearest(ChartPoint clicked, double normalizedDistanceThreshold)
    {
        CalibrationPoint? nearest = null;
        var best = double.MaxValue;
        var xRange = Math.Max(1e-9, Viewport.XMax - Viewport.XMin);
        var yRange = Math.Max(1e-9, Viewport.YMax - Viewport.YMin);
        foreach (var point in CalibrationPoints)
        {
            var dx = (point.X - clicked.X) / xRange;
            var dy = (point.Y - clicked.Y) / yRange;
            var distance = dx * dx + dy * dy;
            if (distance < best)
            {
                best = distance;
                nearest = point;
            }
        }

        return best <= normalizedDistanceThreshold * normalizedDistanceThreshold ? nearest : null;
    }

}
