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
        Color = SKColors.Black
    };

    private readonly SKPaint _pointDisabledStrokePaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1f,
        Color = SKColors.Black
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
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round,
        Color = new SKColor(90, 90, 90, 200)
    };

    private readonly SKPaint _labelTextPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = new SKColor(40, 40, 40)
    };

    private readonly List<(CalibrationPoint Point, SKRect Bounds)> _calibrationLabelHitRegions = [];

    private int _curveLineMutationSeq;
    private int _cachedCurveLineSeq = -1;
    private ChartPoint[] _cachedResolvedCurvePoints = [];

    private void BumpResolvedCurveLineCache() => _curveLineMutationSeq++;

    private ChartPoint[] GetOrBuildResolvedCurvePoints()
    {
        if (_cachedCurveLineSeq == _curveLineMutationSeq)
        {
            return _cachedResolvedCurvePoints;
        }

        if (CurveOverlayPoints is { Count: >= 2 } overlay)
        {
            _cachedResolvedCurvePoints = overlay is ChartPoint[] arr ? arr : overlay.ToArray();
        }
        else
        {
            _cachedResolvedCurvePoints = CalibrationPoints
                .Where(static p => p.IsEnabled && p.PointKind == CalibrationPlotPointKind.CalibrationStandard)
                .OrderBy(static p => p.X)
                .Select(static p => new ChartPoint(p.X, p.Y))
                .ToArray();
        }

        _cachedCurveLineSeq = _curveLineMutationSeq;
        return _cachedResolvedCurvePoints;
    }

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

    public override void ZoomOutCompletely()
    {
        if (!CanInteract())
        {
            return;
        }

        FitViewportToCalibrationPoints();
        NotifyViewportChanged();
        InvalidateVisual();
    }

    protected override void ApplyViewportInteractionClamp()
    {
        if (!TryGetCalibrationZoomExtents(out var xMinLim, out var xMaxLim, out _, out _))
        {
            base.ApplyViewportInteractionClamp();
            return;
        }

        ClampViewportToCustomXWheelZoomLimits(xMinLim, xMaxLim);
    }

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

        BumpResolvedCurveLineCache();
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
                point.IsEnabled ? _pointStrokePaint : _pointDisabledStrokePaint),
        };

    /// <summary>Draws <see cref="CurveOverlayPoints"/> when provided; otherwise a polyline through enabled calibration points.</summary>
    protected override void DrawSeries(SKCanvas canvas, IReadOnlyList<ChartPoint> points, SKRect plotRect)
    {
        _ = points;
        var line = GetOrBuildResolvedCurvePoints();
        if (line.Length < 2)
        {
            return;
        }

        var displayLine = ExtendCalibrationCurveForDisplay(line);
        ChartSkiaLineSeries.DrawPolyline(canvas, displayLine, plotRect, ToPixelX, ToPixelY, _fitPaint);
    }

    private static IReadOnlyList<ChartPoint> ExtendCalibrationCurveForDisplay(IReadOnlyList<ChartPoint> line)
    {
        if (line.Count < 2)
        {
            return line;
        }

        var minX = line[0].X;
        var maxX = line[0].X;
        for (var i = 1; i < line.Count; i++)
        {
            var p = line[i];
            if (p.X < minX)
            {
                minX = p.X;
            }

            if (p.X > maxX)
            {
                maxX = p.X;
            }
        }

        var spanX = Math.Max(1e-12, maxX - minX);
        var extendX = spanX * 0.05;
        var extended = new List<ChartPoint>(line.Count + 2);
        if (TryExtrapolateCurveToX(line[0], line[1], minX - extendX, out var left))
        {
            extended.Add(left);
        }

        extended.AddRange(line);
        if (TryExtrapolateCurveToX(line[^2], line[^1], maxX + extendX, out var right))
        {
            extended.Add(right);
        }

        return extended;
    }

    private static bool TryExtrapolateCurveToX(ChartPoint from, ChartPoint to, double targetX, out ChartPoint extrapolated)
    {
        var dx = to.X - from.X;
        if (Math.Abs(dx) <= 1e-12)
        {
            extrapolated = default;
            return false;
        }

        var t = (targetX - from.X) / dx;
        extrapolated = new ChartPoint(targetX, from.Y + t * (to.Y - from.Y));
        return true;
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
        var borderMargin = 6f * PlotUiScale;
        var placementBounds = GetLabelPlacementBounds(plotRect, borderMargin);
        var curvePx = BuildCurvePolylinePixels(plotRect);
        var markerObstacles = BuildCalibrationMarkerObstacles(plotRect, markerRadiusPx, pad);
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
            var wBox = blockW + 2f * pad;
            var hBox = blockH + 2f * pad;
            var markerClearance = markerRadiusPx + 8f * PlotUiScale;
            if (!TryPlaceCalibrationLabelRect(
                    ax,
                    ay,
                    wBox,
                    hBox,
                    markerRadiusPx,
                    markerClearance,
                    placementBounds,
                    occupied,
                    curvePx,
                    markerObstacles,
                    out var r))
            {
                continue;
            }

            occupied.Add(r);

            var textLeft = r.Left + pad;
            var baseline = r.Top + pad + font.Size * 0.75f;
            foreach (var line in lines)
            {
                canvas.DrawText(line, textLeft, baseline, SKTextAlign.Left, font, _labelTextPaint);
                baseline += lineHeight;
            }

            DrawCalibrationLabelConnector(canvas, new SKPoint(ax, ay), markerRadiusPx, r);

            var hitPad = 3f * PlotUiScale;
            _calibrationLabelHitRegions.Add((point, OutsetRect(r, hitPad, hitPad)));
        }
    }

    private SKRect GetLabelPlacementBounds(SKRect plotRect, float margin)
    {
        var zs = PlotUiScale;
        var axisInset = ChartPlotLayout.AxisLabelInsetFromPlotLeft(zs);
        var left = plotRect.Left + margin + axisInset;
        var right = plotRect.Right - margin;
        var top = plotRect.Top + margin;
        var bottom = plotRect.Bottom - margin;
        if (right <= left || bottom <= top)
        {
            return SKRect.Create(plotRect.MidX, plotRect.MidY, 0, 0);
        }

        return SKRect.Create(left, top, right - left, bottom - top);
    }

    private List<(float X, float Y, float Radius)> BuildCalibrationMarkerObstacles(
        SKRect plotRect,
        float markerRadiusPx,
        float pad)
    {
        var clearance = markerRadiusPx + pad;
        var obstacles = new List<(float X, float Y, float Radius)>();
        foreach (var point in CalibrationPoints)
        {
            if (point.PointKind == CalibrationPlotPointKind.CurrentSample)
            {
                continue;
            }

            obstacles.Add((ToPixelX(point.X, plotRect), ToPixelY(point.Y, plotRect), clearance));
        }

        return obstacles;
    }

    private bool TryPlaceCalibrationLabelRect(
        float ax,
        float ay,
        float width,
        float height,
        float markerRadiusPx,
        float markerClearance,
        SKRect placementBounds,
        IReadOnlyList<SKRect> occupied,
        IReadOnlyList<SKPoint> curvePx,
        IReadOnlyList<(float X, float Y, float Radius)> markerObstacles,
        out SKRect rect)
    {
        rect = default;
        if (placementBounds.Width < width || placementBounds.Height < height)
        {
            return false;
        }

        var spiralStartRadius = markerRadiusPx + 10f * PlotUiScale;
        var angleStep = MathF.PI / 10f;
        var radiusGrowthPerRadian = 5f * PlotUiScale;
        var theta = 0f;
        var bestDistanceSq = float.MaxValue;
        var found = false;
        for (var step = 0; step < 320; step++)
        {
            var radius = spiralStartRadius + radiusGrowthPerRadian * theta;
            var ox = MathF.Cos(theta) * radius;
            var oy = MathF.Sin(theta) * radius;
            theta += angleStep;

            var candidate = SKRect.Create(ax + ox - width * 0.5f, ay + oy - height * 0.5f, width, height);
            var clamped = ClampRectToBounds(candidate, placementBounds);
            if (!IsCalibrationLabelPlacementValid(
                    clamped,
                    ax,
                    ay,
                    markerClearance,
                    occupied,
                    curvePx,
                    markerObstacles))
            {
                continue;
            }

            var dx = clamped.MidX - ax;
            var dy = clamped.MidY - ay;
            var distanceSq = dx * dx + dy * dy;
            if (distanceSq >= bestDistanceSq)
            {
                continue;
            }

            bestDistanceSq = distanceSq;
            rect = clamped;
            found = true;
        }

        return found;
    }

    private static bool IsCalibrationLabelPlacementValid(
        SKRect rect,
        float markerX,
        float markerY,
        float markerClearance,
        IReadOnlyList<SKRect> occupied,
        IReadOnlyList<SKPoint> curvePx,
        IReadOnlyList<(float X, float Y, float Radius)> markerObstacles)
    {
        if (rect.Width <= 0f || rect.Height <= 0f)
        {
            return false;
        }

        if (LabelRectIntrudesMarker(rect, markerX, markerY, markerClearance))
        {
            return false;
        }

        if (IntersectsOccupied(rect, occupied, 4f))
        {
            return false;
        }

        if (RectTooCloseToPolyline(rect, curvePx))
        {
            return false;
        }

        foreach (var (x, y, radius) in markerObstacles)
        {
            if (RectOverlapsCircle(rect, x, y, radius))
            {
                return false;
            }
        }

        return true;
    }

    private static bool RectOverlapsCircle(SKRect rect, float cx, float cy, float radius)
    {
        var qx = Math.Clamp(cx, rect.Left, rect.Right);
        var qy = Math.Clamp(cy, rect.Top, rect.Bottom);
        var dx = cx - qx;
        var dy = cy - qy;
        return dx * dx + dy * dy < radius * radius;
    }

    private static SKRect ClampRectToBounds(SKRect rect, SKRect bounds)
    {
        if (bounds.Width <= 0f || bounds.Height <= 0f)
        {
            return SKRect.Create(bounds.MidX, bounds.MidY, 0, 0);
        }

        var width = Math.Min(rect.Width, bounds.Width);
        var height = Math.Min(rect.Height, bounds.Height);
        var maxLeft = bounds.Right - width;
        var maxTop = bounds.Bottom - height;
        var left = maxLeft < bounds.Left ? bounds.Left : Math.Clamp(rect.Left, bounds.Left, maxLeft);
        var top = maxTop < bounds.Top ? bounds.Top : Math.Clamp(rect.Top, bounds.Top, maxTop);
        return SKRect.Create(left, top, width, height);
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

    private List<SKPoint> BuildCurvePolylinePixels(SKRect plotRect)
    {
        var line = GetOrBuildResolvedCurvePoints();
        if (line.Length < 2)
        {
            return [];
        }

        List<SKPoint> pts = new(line.Length);
        foreach (var p in line)
        {
            pts.Add(new SKPoint(ToPixelX(p.X, plotRect), ToPixelY(p.Y, plotRect)));
        }

        return pts;
    }

    private static bool IntersectsOccupied(SKRect rect, IReadOnlyList<SKRect> occupied, float pad)
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

    private void DrawCalibrationLabelConnector(SKCanvas canvas, SKPoint markerCenter, float markerRadiusPx, SKRect labelRect)
    {
        var labelCenter = new SKPoint(labelRect.MidX, labelRect.MidY);
        if (!TryGetMarkerLabelConnectorPoints(markerCenter, markerRadiusPx, labelCenter, labelRect, out var start, out var end))
        {
            return;
        }

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (dx * dx + dy * dy < 1f)
        {
            return;
        }

        canvas.DrawLine(start, end, _labelConnectorPaint);
    }

    private static bool TryGetMarkerLabelConnectorPoints(
        SKPoint markerCenter,
        float markerRadiusPx,
        SKPoint labelCenter,
        SKRect labelRect,
        out SKPoint start,
        out SKPoint end)
    {
        start = default;
        end = default;
        var dx = labelCenter.X - markerCenter.X;
        var dy = labelCenter.Y - markerCenter.Y;
        var distanceSq = dx * dx + dy * dy;
        if (distanceSq < 1e-6f)
        {
            return false;
        }

        var distance = MathF.Sqrt(distanceSq);
        var minT = markerRadiusPx / distance;
        if (minT >= 1f - 1e-4f)
        {
            return false;
        }

        start = new SKPoint(markerCenter.X + dx * minT, markerCenter.Y + dy * minT);
        if (!TryGetSegmentRectBorderIntersection(markerCenter, labelCenter, labelRect, minT, out end))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetSegmentRectBorderIntersection(
        SKPoint from,
        SKPoint to,
        SKRect rect,
        float minNormalizedT,
        out SKPoint hit)
    {
        hit = default;
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        if (MathF.Abs(dx) < 1e-3f && MathF.Abs(dy) < 1e-3f)
        {
            return false;
        }

        var bestT = float.PositiveInfinity;
        var found = false;
        var hitPoint = default(SKPoint);
        const float edgeTolerance = 0.75f;
        const float tEpsilon = 1e-4f;

        void Consider(float t, float x, float y)
        {
            if (t <= minNormalizedT + tEpsilon || t > 1f + tEpsilon || t >= bestT)
            {
                return;
            }

            bestT = t;
            found = true;
            hitPoint = new SKPoint(x, y);
        }

        if (MathF.Abs(dx) > 1e-3f)
        {
            var tLeft = (rect.Left - from.X) / dx;
            var yLeft = from.Y + dy * tLeft;
            if (yLeft >= rect.Top - edgeTolerance && yLeft <= rect.Bottom + edgeTolerance)
            {
                Consider(tLeft, rect.Left, yLeft);
            }

            var tRight = (rect.Right - from.X) / dx;
            var yRight = from.Y + dy * tRight;
            if (yRight >= rect.Top - edgeTolerance && yRight <= rect.Bottom + edgeTolerance)
            {
                Consider(tRight, rect.Right, yRight);
            }
        }

        if (MathF.Abs(dy) > 1e-3f)
        {
            var tTop = (rect.Top - from.Y) / dy;
            var xTop = from.X + dx * tTop;
            if (xTop >= rect.Left - edgeTolerance && xTop <= rect.Right + edgeTolerance)
            {
                Consider(tTop, xTop, rect.Top);
            }

            var tBottom = (rect.Bottom - from.Y) / dy;
            var xBottom = from.X + dx * tBottom;
            if (xBottom >= rect.Left - edgeTolerance && xBottom <= rect.Right + edgeTolerance)
            {
                Consider(tBottom, xBottom, rect.Bottom);
            }
        }

        hit = hitPoint;
        return found;
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

        if (TryEvaluateCurveYAtX(curve, guideX, out var curveYAtGuideX))
        {
            var px = ToPixelX(guideX, plotRect);
            var pyAxis = plotRect.Bottom;
            var pyCurve = ToPixelY(curveYAtGuideX, plotRect);
            canvas.DrawLine(px, pyAxis, px, pyCurve, _currentQuantifierGuidePaint);

            if (TryEvaluateCurveXAtY(curve, guideY, guideX, out var curveXAtGuideY))
            {
                var pxCurve = ToPixelX(curveXAtGuideY, plotRect);
                var py = ToPixelY(guideY, plotRect);
                canvas.DrawLine(plotRect.Left, py, pxCurve, py, _currentQuantifierGuidePaint);
            }
            else
            {
                canvas.DrawLine(plotRect.Left, pyCurve, px, pyCurve, _currentQuantifierGuidePaint);
            }
        }
        else if (TryEvaluateCurveXAtY(curve, guideY, guideX, out var curveXAtGuideY))
        {
            var pxAxis = plotRect.Left;
            var pxCurve = ToPixelX(curveXAtGuideY, plotRect);
            var py = ToPixelY(guideY, plotRect);
            canvas.DrawLine(pxAxis, py, pxCurve, py, _currentQuantifierGuidePaint);
        }
    }

    private IReadOnlyList<ChartPoint> GetCurveChartPoints()
    {
        var pts = GetOrBuildResolvedCurvePoints();
        return pts.Length >= 2 ? pts : Array.Empty<ChartPoint>();
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

    private static bool TryEvaluateCurveYAtX(IReadOnlyList<ChartPoint> curve, double x, out double y)
    {
        if (TryInterpolateCurveYAtX(curve, x, out y))
        {
            return true;
        }

        if (curve.Count < 2)
        {
            return false;
        }

        var first = curve[0];
        var second = curve[1];
        if (x < Math.Min(first.X, second.X) - 1e-12)
        {
            return TryExtrapolateLinearY(first, second, x, out y);
        }

        var last = curve[^1];
        var previous = curve[^2];
        if (x > Math.Max(last.X, previous.X) + 1e-12)
        {
            return TryExtrapolateLinearY(previous, last, x, out y);
        }

        return false;
    }

    private static bool TryEvaluateCurveXAtY(
        IReadOnlyList<ChartPoint> curve,
        double y,
        double preferredX,
        out double x)
    {
        if (TryInterpolateCurveXAtY(curve, y, preferredX, out x))
        {
            return true;
        }

        if (curve.Count < 2)
        {
            return false;
        }

        var bestDistance = double.PositiveInfinity;
        var found = false;
        for (var i = 0; i < curve.Count - 1; i++)
        {
            var a = curve[i];
            var b = curve[i + 1];
            if (!TryExtrapolateLinearX(a, b, y, out var candidateX))
            {
                continue;
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

    private static bool TryExtrapolateLinearY(ChartPoint a, ChartPoint b, double x, out double y)
    {
        y = 0;
        if (Math.Abs(b.X - a.X) <= 1e-12)
        {
            y = (a.Y + b.Y) * 0.5;
            return double.IsFinite(y);
        }

        var t = (x - a.X) / (b.X - a.X);
        y = a.Y + t * (b.Y - a.Y);
        return double.IsFinite(y);
    }

    private static bool TryExtrapolateLinearX(ChartPoint a, ChartPoint b, double y, out double x)
    {
        x = 0;
        if (Math.Abs(b.Y - a.Y) <= 1e-12)
        {
            x = (a.X + b.X) * 0.5;
            return double.IsFinite(x);
        }

        var t = (y - a.Y) / (b.Y - a.Y);
        x = a.X + t * (b.X - a.X);
        return double.IsFinite(x);
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
        c.BumpResolvedCurveLineCache();
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
        c.BumpResolvedCurveLineCache();
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
        if (!TryGetCalibrationZoomExtents(out var xMin, out var xMax, out var yMin, out var yMax))
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

    private bool TryGetCalibrationZoomExtents(
        out double xMin,
        out double xMax,
        out double yMin,
        out double yMax) =>
        CalibrationViewportBounds.TryGetZoomOutExtents(
            CalibrationPoints,
            CurveOverlayPoints,
            BuildQuantifierGuideExtentPoints(),
            out xMin,
            out xMax,
            out yMin,
            out yMax);

    private ChartPoint[]? BuildQuantifierGuideExtentPoints()
    {
        var guideX = CurrentQuantifierGuideX;
        var guideY = CurrentQuantifierGuideY;
        if (!double.IsFinite(guideX) || !double.IsFinite(guideY))
        {
            return null;
        }

        var extents = new List<ChartPoint>(3) { new(guideX, guideY) };
        var curve = GetCurveChartPoints();
        if (curve.Count >= 2)
        {
            if (TryInterpolateCurveYAtX(curve, guideX, out var curveYAtGuideX))
            {
                extents.Add(new ChartPoint(guideX, curveYAtGuideX));
            }
            else if (TryEvaluateCurveYAtX(curve, guideX, out var extrapolatedYAtGuideX))
            {
                extents.Add(new ChartPoint(guideX, extrapolatedYAtGuideX));
            }

            if (TryInterpolateCurveXAtY(curve, guideY, guideX, out var curveXAtGuideY))
            {
                extents.Add(new ChartPoint(curveXAtGuideY, guideY));
            }
            else if (TryEvaluateCurveXAtY(curve, guideY, guideX, out var extrapolatedXAtGuideY))
            {
                extents.Add(new ChartPoint(extrapolatedXAtGuideY, guideY));
            }
        }

        return extents.ToArray();
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
