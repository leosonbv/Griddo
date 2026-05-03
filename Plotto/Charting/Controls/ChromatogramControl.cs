using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SkiaSharp;
using Plotto.Charting.Core;
using Plotto.Charting.Rendering;

namespace Plotto.Charting.Controls;

public partial class ChromatogramControl : SkiaChartBaseControl
{
    private const double ActivationMoveToleranceDip = 4d;
    private Point _activationPressPosition;
    private bool _awaitingActivationClick;

    private IntegrationRegion? _activeRegion;
    private bool _isIntegrationDragActive;
    private readonly List<double> _peakSplitStaticX = [];
    private double? _peakSplitHoverX;
    private readonly SKPaint _integrationFillPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = new SKColor(200, 255, 170, 110)
    };

    private readonly SKPaint _integrationLinePaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1.5f,
        Color = SKColors.Red
    };

    public event EventHandler<IntegrationRegionEventArgs>? IntegrationChanged;

    public ChromatogramControl()
    {
        var descriptor = DependencyPropertyDescriptor.FromProperty(
            SkiaChartBaseControl.RenderModeProperty,
            typeof(ChromatogramControl));
        descriptor?.AddValueChanged(this, (_, _) => OnRenderModeChanged());
    }

    protected override void ApplyUiScaleToResources()
    {
        base.ApplyUiScaleToResources();
        _integrationLinePaint.StrokeWidth = Math.Max(0.5f, 1.5f * PlotUiScale);
    }

    /// <summary>
    /// Caps vertical zoom-out: visible Y span cannot exceed <c>ymaxPlot / 0.5</c> (twice the max Y in the trace).
    /// Does not run on Y zoom-in or on pan.
    /// </summary>
    protected override void OnAfterYZoom(double scale)
    {
        if (scale <= 1.0 + 1e-15)
        {
            return;
        }

        var points = Points;
        if (points.Count == 0)
        {
            return;
        }

        ChartSeriesBounds.GetExtents(points, out _, out _, out _, out var ymaxPlot);
        const double eps = 1e-12;
        if (ymaxPlot <= eps)
        {
            return;
        }

        var maxSpan = ymaxPlot / 0.5;
        var h = Viewport.YMax - Viewport.YMin;
        if (h <= maxSpan + eps)
        {
            return;
        }

        var mid = (Viewport.YMin + Viewport.YMax) * 0.5;
        Viewport.YMin = mid - maxSpan * 0.5;
        Viewport.YMax = mid + maxSpan * 0.5;
        Viewport.EnsureMinimumSize();
        ApplyViewportInteractionClamp();
    }

    public IReadOnlyList<IntegrationRegion> IntegrationRegions
    {
        get => (IReadOnlyList<IntegrationRegion>)GetValue(IntegrationRegionsProperty);
        set => SetValue(IntegrationRegionsProperty, value);
    }

    public static readonly DependencyProperty IntegrationRegionsProperty =
        DependencyProperty.Register(
            nameof(IntegrationRegions),
            typeof(IReadOnlyList<IntegrationRegion>),
            typeof(ChromatogramControl),
            new FrameworkPropertyMetadata(Array.Empty<IntegrationRegion>(), FrameworkPropertyMetadataOptions.AffectsRender));

    private void OnRenderModeChanged()
    {
        _awaitingActivationClick = false;
        _isIntegrationDragActive = false;
        _peakSplitStaticX.Clear();
        _peakSplitHoverX = null;

        if (RenderMode == ChartRenderMode.Editor)
        {
            IntegrationRegions = Array.Empty<IntegrationRegion>();
            _activeRegion = null;
        }
        else if (RenderMode == ChartRenderMode.Renderer)
        {
            IntegrationRegions = Array.Empty<IntegrationRegion>();
            _activeRegion = null;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Clears manual integration geometry and peak-split overlays without changing <see cref="SkiaChartBaseControl.RenderMode"/>.
    /// Used when the chart is rebound to another dataset (e.g. shared editor moved to another cell).
    /// </summary>
    public void ResetIntegrationDisplay()
    {
        _isIntegrationDragActive = false;
        _peakSplitStaticX.Clear();
        _peakSplitHoverX = null;
        IntegrationRegions = Array.Empty<IntegrationRegion>();
        _activeRegion = null;
        InvalidateVisual();
    }

    protected override void DrawOverlay(SKCanvas canvas, SKRect plotRect)
    {
        var ordered = Points.Count == 0
            ? (IReadOnlyList<ChartPoint>)Array.Empty<ChartPoint>()
            : Points.OrderBy(p => p.X).ToList();

        foreach (var region in IntegrationRegions)
        {
            ChartSkiaManualIntegration.DrawRegionFill(canvas, ordered, plotRect, region, ToPixelX, ToPixelY, _integrationFillPaint);
        }

        if (_activeRegion is { } activeFill)
        {
            ChartSkiaManualIntegration.DrawRegionFill(canvas, ordered, plotRect, activeFill, ToPixelX, ToPixelY, _integrationFillPaint);
        }

        foreach (var region in IntegrationRegions)
        {
            ChartSkiaManualIntegration.DrawRegionBaseline(canvas, ordered, plotRect, region, ToPixelX, ToPixelY, _integrationLinePaint);
        }

        if (_activeRegion is { } activeLine)
        {
            ChartSkiaManualIntegration.DrawRegionBaseline(canvas, ordered, plotRect, activeLine, ToPixelX, ToPixelY, _integrationLinePaint);
        }

        if (RenderMode == ChartRenderMode.Editor && TryGetPrimaryIntegrationRegion(out var splitRef))
        {
            foreach (var xSplit in _peakSplitStaticX)
            {
                ChartSkiaManualIntegration.DrawPeakSplitVertical(canvas, ordered, plotRect, xSplit, splitRef, ToPixelX, ToPixelY, _integrationLinePaint);
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0
                && Mouse.LeftButton == MouseButtonState.Released
                && _peakSplitHoverX is { } hx)
            {
                ChartSkiaManualIntegration.DrawPeakSplitVertical(canvas, ordered, plotRect, hx, splitRef, ToPixelX, ToPixelY, _integrationLinePaint);
            }
        }
    }

    private bool TryGetPrimaryIntegrationRegion(out IntegrationRegion region)
    {
        if (_activeRegion is { } a)
        {
            region = a;
            return true;
        }

        if (IntegrationRegions.Count > 0)
        {
            region = IntegrationRegions[0];
            return true;
        }

        region = default;
        return false;
    }

    /// <summary>
    /// Keeps baseline endpoints at or below the chromatogram (data Y ≤ signal at that X).
    /// </summary>
    private ChartPoint ClampBaselineAnchor(ChartPoint point)
    {
        var pts = Points;
        if (pts.Count == 0)
        {
            return point;
        }

        var ordered = pts.OrderBy(p => p.X).ToList();
        var yCurve = ChartSignalInterpolation.InterpolateYAtX(ordered, point.X);
        return new ChartPoint(point.X, Math.Min(point.Y, yCurve));
    }
}
