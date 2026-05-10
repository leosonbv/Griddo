
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SkiaSharp;
using Plotto.Charting.Core;
using Plotto.Charting.Rendering;

namespace Plotto.Charting.Controls;

public partial class ChromatogramControl : SkiaChartBaseControl
{
    /// <summary>Multiplies committed manual-peak fill RGB (same alpha as normal peak fill); lower is darker.</summary>
    private const float ManualIntegratedPeakFillRgbFactor = 0.68f;

    private const double ActivationMoveToleranceDip = 4d;
    private Point _activationPressPosition;
    private bool _awaitingActivationClick;

    private IntegrationRegion? _activeRegion;
    private bool _isIntegrationDragActive;
    private double _overlayLineWidthDip = 1.5d;
    private readonly List<double> _peakSplitStaticX = [];
    private double? _peakSplitHoverX;
    private readonly SKPaint _integrationFillPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = new SKColor(80, 30, 144, 255)
    };

    private readonly SKPaint _integrationLinePaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1.5f,
        Color = SKColors.Red
    };
    private readonly SKPaint _selectedPeakFillPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = new SKColor(80, 220, 120, 96)
    };
    private readonly SKPaint _selectedPeakLinePaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1.5f,
        Color = new SKColor(22, 163, 74)
    };
    private readonly SKPaint _alternativePeakFillPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = new SKColor(255, 235, 59, 96)
    };
    private readonly SKPaint _alternativeManualPeakFillPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = new SKColor(173, 160, 40, 96)
    };
    private readonly SKPaint _selectedManualPeakFillPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = new SKColor(54, 150, 82, 96)
    };
    private readonly SKPaint _alternativePeakLinePaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1.5f,
        Color = new SKColor(234, 179, 8)
    };
    private readonly SKPaint _rendererIntegrationLinePaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1.5f,
        Color = SKColors.Red
    };

    public event EventHandler<IntegrationRegionEventArgs>? IntegrationChanged;
    public event EventHandler<PeakSplitEventArgs>? PeakSplitRequested;
    public event EventHandler<PeakSelectionEventArgs>? PeakSelectionRequested;

    public ChromatogramControl()
    {
        var descriptor = DependencyPropertyDescriptor.FromProperty(
            SkiaChartBaseControl.RenderModeProperty,
            typeof(ChromatogramControl));
        descriptor?.AddValueChanged(this, (_, _) => OnRenderModeChanged());
    }

    public void SetPeakOverlayColors(
        SKColor selectedPeak,
        SKColor alternativePeak,
        SKColor integrationLine,
        SKColor manualIntegrationFill,
        double overlayLineWidthDip,
        int peakFillAlpha)
    {
        var clampedAlpha = (byte)Math.Clamp(peakFillAlpha, 0, 255);
        _overlayLineWidthDip = Math.Max(0.5d, overlayLineWidthDip);
        _selectedPeakLinePaint.Color = selectedPeak;
        _alternativePeakLinePaint.Color = alternativePeak;
        _integrationLinePaint.Color = integrationLine;
        _rendererIntegrationLinePaint.Color = SKColors.Red;
        _integrationFillPaint.Color = new SKColor(
            manualIntegrationFill.Red,
            manualIntegrationFill.Green,
            manualIntegrationFill.Blue,
            clampedAlpha);
        _selectedPeakFillPaint.Color = new SKColor(selectedPeak.Red, selectedPeak.Green, selectedPeak.Blue, clampedAlpha);
        _alternativePeakFillPaint.Color = new SKColor(alternativePeak.Red, alternativePeak.Green, alternativePeak.Blue, clampedAlpha);
        _selectedManualPeakFillPaint.Color = DarkenPeakFillRgb(_selectedPeakFillPaint.Color, ManualIntegratedPeakFillRgbFactor);
        _alternativeManualPeakFillPaint.Color = DarkenPeakFillRgb(_alternativePeakFillPaint.Color, ManualIntegratedPeakFillRgbFactor);
        ApplyUiScaleToResources();
    }

    private static SKColor DarkenPeakFillRgb(SKColor fill, float rgbFactor)
    {
        byte Scale(byte channel)
        {
            var v = (int)Math.Round(channel * rgbFactor);
            return (byte)Math.Clamp(v, 0, 255);
        }

        return new SKColor(Scale(fill.Red), Scale(fill.Green), Scale(fill.Blue), fill.Alpha);
    }

    protected override void ApplyUiScaleToResources()
    {
        base.ApplyUiScaleToResources();
        var stroke = Math.Max(0.5f, (float)(_overlayLineWidthDip * PlotUiScale));
        _integrationLinePaint.StrokeWidth = stroke;
        _selectedPeakLinePaint.StrokeWidth = stroke;
        _alternativePeakLinePaint.StrokeWidth = stroke;
        _rendererIntegrationLinePaint.StrokeWidth = stroke;
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

    public IReadOnlyList<IntegrationRegion> AlternativeIntegrationRegions
    {
        get => (IReadOnlyList<IntegrationRegion>)GetValue(AlternativeIntegrationRegionsProperty);
        set => SetValue(AlternativeIntegrationRegionsProperty, value);
    }

    public static readonly DependencyProperty AlternativeIntegrationRegionsProperty =
        DependencyProperty.Register(
            nameof(AlternativeIntegrationRegions),
            typeof(IReadOnlyList<IntegrationRegion>),
            typeof(ChromatogramControl),
            new FrameworkPropertyMetadata(Array.Empty<IntegrationRegion>(), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Parallel to <see cref="AlternativeIntegrationRegions"/>; when shorter than that list, missing entries are treated as false.
    /// When true, the region fill uses the manual-integration color instead of the alternative-peak overlay color.
    /// </summary>
    public IReadOnlyList<bool> AlternativeIntegrationRegionsManualIntegrated
    {
        get => (IReadOnlyList<bool>)GetValue(AlternativeIntegrationRegionsManualIntegratedProperty);
        set => SetValue(AlternativeIntegrationRegionsManualIntegratedProperty, value);
    }

    public static readonly DependencyProperty AlternativeIntegrationRegionsManualIntegratedProperty =
        DependencyProperty.Register(
            nameof(AlternativeIntegrationRegionsManualIntegrated),
            typeof(IReadOnlyList<bool>),
            typeof(ChromatogramControl),
            new FrameworkPropertyMetadata(Array.Empty<bool>(), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Parallel to <see cref="IntegrationRegions"/>; when shorter than that list, missing entries are treated as false.
    /// When true, the region fill uses the manual-integration color instead of the selected-peak overlay color.
    /// </summary>
    public IReadOnlyList<bool> IntegrationRegionsManualIntegrated
    {
        get => (IReadOnlyList<bool>)GetValue(IntegrationRegionsManualIntegratedProperty);
        set => SetValue(IntegrationRegionsManualIntegratedProperty, value);
    }

    public static readonly DependencyProperty IntegrationRegionsManualIntegratedProperty =
        DependencyProperty.Register(
            nameof(IntegrationRegionsManualIntegrated),
            typeof(IReadOnlyList<bool>),
            typeof(ChromatogramControl),
            new FrameworkPropertyMetadata(Array.Empty<bool>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<ColoredIntegrationRegion> ColoredIntegrationRegions
    {
        get => (IReadOnlyList<ColoredIntegrationRegion>)GetValue(ColoredIntegrationRegionsProperty);
        set => SetValue(ColoredIntegrationRegionsProperty, value);
    }

    public static readonly DependencyProperty ColoredIntegrationRegionsProperty =
        DependencyProperty.Register(
            nameof(ColoredIntegrationRegions),
            typeof(IReadOnlyList<ColoredIntegrationRegion>),
            typeof(ChromatogramControl),
            new FrameworkPropertyMetadata(Array.Empty<ColoredIntegrationRegion>(), FrameworkPropertyMetadataOptions.AffectsRender));

    private void OnRenderModeChanged()
    {
        _awaitingActivationClick = false;
        _isIntegrationDragActive = false;
        _peakSplitStaticX.Clear();
        _peakSplitHoverX = null;
        _activeRegion = null;

        // Do not clear IntegrationRegions / AlternativeIntegrationRegions / ColoredIntegrationRegions here.
        // The grid host pushes those from data (SyncPeakOverlay); clearing on mode flip removes peaks when
        // zoom switches Renderer → Editor (right-drag rubber band) until the next host refresh.

        if (RenderMode == ChartRenderMode.Renderer && IsMouseCaptured)
        {
            ReleaseMouseCapture();
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
        AlternativeIntegrationRegions = Array.Empty<IntegrationRegion>();
        IntegrationRegionsManualIntegrated = Array.Empty<bool>();
        AlternativeIntegrationRegionsManualIntegrated = Array.Empty<bool>();
        ColoredIntegrationRegions = Array.Empty<ColoredIntegrationRegion>();
        _activeRegion = null;
        InvalidateVisual();
    }

    protected override void DrawOverlay(SKCanvas canvas, SKRect plotRect)
    {
        var ordered = Points.Count == 0
            ? (IReadOnlyList<ChartPoint>)Array.Empty<ChartPoint>()
            : Points.OrderBy(p => p.X).ToList();

        var altManualFlags = AlternativeIntegrationRegionsManualIntegrated;
        for (var i = 0; i < AlternativeIntegrationRegions.Count; i++)
        {
            var region = AlternativeIntegrationRegions[i];
            var fillPaint = ManualIntegratedFlagAt(altManualFlags, i)
                ? _alternativeManualPeakFillPaint
                : _alternativePeakFillPaint;
            ChartSkiaManualIntegration.DrawRegionFill(canvas, ordered, plotRect, region, ToPixelX, ToPixelY, fillPaint);
        }

        foreach (var region in AlternativeIntegrationRegions)
        {
            ChartSkiaManualIntegration.DrawRegionBaseline(canvas, ordered, plotRect, region, ToPixelX, ToPixelY, _rendererIntegrationLinePaint);
        }

        var selManualFlags = IntegrationRegionsManualIntegrated;
        for (var i = 0; i < IntegrationRegions.Count; i++)
        {
            var region = IntegrationRegions[i];
            var fillPaint = ManualIntegratedFlagAt(selManualFlags, i)
                ? _selectedManualPeakFillPaint
                : _selectedPeakFillPaint;
            ChartSkiaManualIntegration.DrawRegionFill(canvas, ordered, plotRect, region, ToPixelX, ToPixelY, fillPaint);
        }

        foreach (var region in IntegrationRegions)
        {
            ChartSkiaManualIntegration.DrawRegionBaseline(canvas, ordered, plotRect, region, ToPixelX, ToPixelY, _rendererIntegrationLinePaint);
        }

        if (ColoredIntegrationRegions.Count > 0)
        {
            var fillPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            var linePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = _rendererIntegrationLinePaint.StrokeWidth
            };
            foreach (var colored in ColoredIntegrationRegions)
            {
                var shapePoints = colored.ShapePoints is { Count: > 0 }
                    ? (IReadOnlyList<ChartPoint>)colored.ShapePoints.OrderBy(static p => p.X).ToList()
                    : ordered;
                var fillAlpha = (byte)Math.Clamp(colored.A / 2, 16, 140);
                fillPaint.Color = new SKColor(colored.R, colored.G, colored.B, fillAlpha);
                linePaint.Color = new SKColor(colored.R, colored.G, colored.B, 255);
                ChartSkiaManualIntegration.DrawRegionFill(
                    canvas,
                    shapePoints,
                    plotRect,
                    colored.Region,
                    ToPixelX,
                    ToPixelY,
                    fillPaint);
                ChartSkiaManualIntegration.DrawRegionBaseline(
                    canvas,
                    shapePoints,
                    plotRect,
                    colored.Region,
                    ToPixelX,
                    ToPixelY,
                    linePaint);
                ChartSkiaManualIntegration.DrawRegionSignalLine(
                    canvas,
                    shapePoints,
                    plotRect,
                    colored.Region,
                    ToPixelX,
                    ToPixelY,
                    linePaint);
            }
        }

        if (RenderMode == ChartRenderMode.Renderer)
        {
            return;
        }

        if (_activeRegion is { } activeFill)
        {
            ChartSkiaManualIntegration.DrawRegionFill(canvas, ordered, plotRect, activeFill, ToPixelX, ToPixelY, _integrationFillPaint);
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

    private static bool ManualIntegratedFlagAt(IReadOnlyList<bool> flags, int index) =>
        (uint)index < (uint)flags.Count && flags[index];

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
