using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using Plotto.Charting.Core;
using Plotto.Charting.Geometry;
using Plotto.Charting.Services;
using Plotto.Charting.Viewport;

namespace Plotto.Charting.Controls;

public abstract partial class SkiaChartBaseControl : SKElement
{
    private static PlottoThemeKind _defaultTheme = PlottoThemeKind.Vs2013LightTheme;
    private static event Action<PlottoThemeKind>? DefaultThemeChanged;
    public static PlottoThemeKind DefaultTheme
    {
        get => _defaultTheme;
        set
        {
            if (_defaultTheme == value)
            {
                return;
            }

            _defaultTheme = value;
            DefaultThemeChanged?.Invoke(value);
        }
    }
    /// <summary>Default plot stroke; matches <see cref="CalibrationCurveControl"/> fit line.</summary>
    private readonly SKPaint _linePaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 2f,
        Color = new SKColor(65, 135, 225)
    };
    protected readonly SKPaint AxisStrokePaint = new() { IsAntialias = false, StrokeWidth = 1f, Style = SKPaintStyle.Stroke, Color = SKColors.Gray };
    protected readonly SKPaint AxisLabelPaint = new() { IsAntialias = true, Color = SKColors.Gray };
    protected readonly SKFont AxisFont = new() { Size = 10f };
    private readonly SKPaint _overlayFill = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(80, 180, 250, 70) };
    private readonly SKPaint _overlayStroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = new SKColor(80, 180, 250) };
    /// <summary>Right-drag zoom rectangle fill — translucent blue similar to grid selection tint.</summary>
    private readonly SKPaint _zoomRubberFillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(102, 178, 255, 100) };
    private readonly SKPaint _zoomRubberStrokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = new SKColor(30, 90, 200) };
    private readonly ChartCoordinateMapper _coordinates;
    private Point _lastMousePos;
    private bool _isPanning;
    private bool _isDragging;
    private bool _isRightDragZoom;
    private Point _zoomRectStart;
    private Point _zoomRectCurrent;

    /// <summary>Set only while <see cref="ApplyRightDragZoom"/> invokes <see cref="ApplyViewportInteractionClamp"/>.</summary>
    private bool _viewportClampAfterRectZoom;

    /// <summary>Calibration (and other overrides) can preserve rubber-band X/Y when true; wheel/pan use full clamp.</summary>
    protected bool IsViewportClampAfterRectZoom => _viewportClampAfterRectZoom;

    /// <summary>Right-drag zoom box: capture only after movement so a plain click is not a drag zoom.</summary>
    private bool _rightZoomRubberPending;

    /// <summary>Deferred open so double right-click can reach the chart before the menu popup steals focus.</summary>
    private readonly DeferredDispatcherTimer _deferredContextMenu = new();

    private Point _deferredContextMenuPosition;
    private Point _rightMouseDownPosition;

    /// <summary>Slightly above typical OS double-click time so the second click is recognized before the menu opens.</summary>
    private const int DeferredContextMenuDelayMs = 450;

    private readonly SeriesViewportInteractionClamp _viewportWheelClamp = new();
    private PlottoThemeKind _theme = DefaultTheme;
    protected SKColor ChartBackgroundColor { get; private set; } = SKColors.White;
    protected SKColor PlotBackgroundColor { get; private set; } = SKColors.White;
    protected SKColor TitleForegroundColor { get; private set; } = SKColors.Black;

    protected SkiaChartBaseControl()
    {
        _coordinates = new ChartCoordinateMapper(Viewport);
        Focusable = true;
        IsHitTestVisible = true;
        SnapsToDevicePixels = true;
        Loaded += OnLoadedThemeRegistration;
        Unloaded += OnUnloadedThemeRegistration;
        ApplyUiScaleToResources();
        ApplyTheme(_theme);
    }

    private void OnLoadedThemeRegistration(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        DefaultThemeChanged -= HandleDefaultThemeChanged;
        DefaultThemeChanged += HandleDefaultThemeChanged;
        if (_theme != DefaultTheme)
        {
            ApplyTheme(DefaultTheme);
        }
    }

    private void OnUnloadedThemeRegistration(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        DefaultThemeChanged -= HandleDefaultThemeChanged;
    }

    private void HandleDefaultThemeChanged(PlottoThemeKind theme)
    {
        if (_theme == theme)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() => ApplyTheme(theme)));
    }

    protected virtual void ApplyUiScaleToResources()
    {
        var s = PlotUiScale;
        _linePaint.StrokeWidth = 2f * s;
        AxisStrokePaint.StrokeWidth = Math.Max(0.5f, 1f * s);
        AxisFont.Size = (float)Math.Max(6d, AxisFontSize) * s;
        _overlayStroke.StrokeWidth = Math.Max(0.5f, 1f * s);
        _zoomRubberStrokePaint.StrokeWidth = Math.Max(0.5f, 1f * s);
    }

    public PlottoThemeKind Theme
    {
        get => _theme;
        set
        {
            if (_theme == value)
            {
                return;
            }

            ApplyTheme(value);
        }
    }

    public void ApplyTheme(PlottoThemeKind theme)
    {
        _theme = theme;
        switch (theme)
        {
            case PlottoThemeKind.Vs2013DarkTheme:
                ChartBackgroundColor = new SKColor(37, 37, 38);
                PlotBackgroundColor = new SKColor(45, 45, 48);
                TitleForegroundColor = SKColors.White;
                AxisStrokePaint.Color = new SKColor(104, 104, 104);
                AxisLabelPaint.Color = new SKColor(241, 241, 241);
                _linePaint.Color = new SKColor(86, 156, 214);
                break;
            case PlottoThemeKind.Vs2013LightTheme:
            default:
                ChartBackgroundColor = SKColors.White;
                PlotBackgroundColor = SKColors.White;
                TitleForegroundColor = SKColors.Black;
                AxisStrokePaint.Color = SKColors.Gray;
                AxisLabelPaint.Color = SKColors.Gray;
                _linePaint.Color = new SKColor(65, 135, 225);
                break;
        }

        InvalidateVisual();
    }

    /// <summary>SKElement is not a <see cref="Control"/>; ensure the full bounds participate in hit-testing so mouse zoom/pan work.</summary>
    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        var pt = hitTestParameters.HitPoint;
        if (pt.X < 0 || pt.Y < 0 || pt.X > RenderSize.Width || pt.Y > RenderSize.Height)
        {
            return null;
        }

        return new PointHitTestResult(this, pt);
    }

    public IReadOnlyList<ChartPoint> Points
    {
        get => (IReadOnlyList<ChartPoint>)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(
            nameof(Points),
            typeof(IReadOnlyList<ChartPoint>),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(Array.Empty<ChartPoint>(), FrameworkPropertyMetadataOptions.AffectsRender, OnDataChanged));

    public ChartRenderMode RenderMode
    {
        get => (ChartRenderMode)GetValue(RenderModeProperty);
        set => SetValue(RenderModeProperty, value);
    }

    public static readonly DependencyProperty RenderModeProperty =
        DependencyProperty.Register(
            nameof(RenderMode),
            typeof(ChartRenderMode),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(ChartRenderMode.Editor, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// When true (e.g. Plotto in a grid), full chart interaction uses <see cref="ChartRenderMode.Editor"/> only after a qualifying click
    /// (mouse down and up at the same location). Until then <see cref="ChartRenderMode.Renderer"/> is used.
    /// </summary>
    public bool RequireActivationClick
    {
        get => (bool)GetValue(RequireActivationClickProperty);
        set => SetValue(RequireActivationClickProperty, value);
    }

    public static readonly DependencyProperty RequireActivationClickProperty =
        DependencyProperty.Register(
            nameof(RequireActivationClick),
            typeof(bool),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// When true with <see cref="RequireActivationClick"/>, leaves renderer activation to the parent grid (which switches to
    /// <see cref="ChartRenderMode.Editor"/> and re-raises the mouse down). Avoids capturing the first click on the chart.
    /// </summary>
    public bool DeferRendererActivationToParent
    {
        get => (bool)GetValue(DeferRendererActivationToParentProperty);
        set => SetValue(DeferRendererActivationToParentProperty, value);
    }

    public static readonly DependencyProperty DeferRendererActivationToParentProperty =
        DependencyProperty.Register(
            nameof(DeferRendererActivationToParent),
            typeof(bool),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(false));

    public bool EnableMouseInteractions
    {
        get => (bool)GetValue(EnableMouseInteractionsProperty);
        set => SetValue(EnableMouseInteractionsProperty, value);
    }

    public static readonly DependencyProperty EnableMouseInteractionsProperty =
        DependencyProperty.Register(
            nameof(EnableMouseInteractions),
            typeof(bool),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(true));

    public double SparklineWidthThreshold
    {
        get => (double)GetValue(SparklineWidthThresholdProperty);
        set => SetValue(SparklineWidthThresholdProperty, value);
    }

    public static readonly DependencyProperty SparklineWidthThresholdProperty =
        DependencyProperty.Register(
            nameof(SparklineWidthThreshold),
            typeof(double),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(120d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double SparklineHeightThreshold
    {
        get => (double)GetValue(SparklineHeightThresholdProperty);
        set => SetValue(SparklineHeightThresholdProperty, value);
    }

    public static readonly DependencyProperty SparklineHeightThresholdProperty =
        DependencyProperty.Register(
            nameof(SparklineHeightThreshold),
            typeof(double),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(60d, FrameworkPropertyMetadataOptions.AffectsRender));

    public ChartViewport Viewport { get; } = new();

    /// <summary>
    /// When true, the next assignment to <see cref="Points"/> skips <see cref="FitViewportToCurrentPoints"/> so callers can restore a saved viewport first.
    /// Cleared automatically after that assignment. When the new points list is empty, the viewport is still updated from data.
    /// </summary>
    public bool SuppressAutomaticViewportFitOnNextPointsChange { get; set; }

    public event EventHandler? ViewportChanged;
    public event EventHandler<ChartPointEventArgs>? DataPointClicked;
    public event EventHandler? PopupEditorRequested;

    public bool EnableInlineEditing
    {
        get => (bool)GetValue(EnableInlineEditingProperty);
        set => SetValue(EnableInlineEditingProperty, value);
    }

    public static readonly DependencyProperty EnableInlineEditingProperty =
        DependencyProperty.Register(
            nameof(EnableInlineEditing),
            typeof(bool),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(true));

    public bool EnablePopupEditing
    {
        get => (bool)GetValue(EnablePopupEditingProperty);
        set => SetValue(EnablePopupEditingProperty, value);
    }

    public static readonly DependencyProperty EnablePopupEditingProperty =
        DependencyProperty.Register(
            nameof(EnablePopupEditing),
            typeof(bool),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(true));

    public string ChartTitle
    {
        get => (string)GetValue(ChartTitleProperty);
        set => SetValue(ChartTitleProperty, value);
    }

    public static readonly DependencyProperty ChartTitleProperty =
        DependencyProperty.Register(
            nameof(ChartTitle),
            typeof(string),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool ShowChartTitle
    {
        get => (bool)GetValue(ShowChartTitleProperty);
        set => SetValue(ShowChartTitleProperty, value);
    }

    public static readonly DependencyProperty ShowChartTitleProperty =
        DependencyProperty.Register(
            nameof(ShowChartTitle),
            typeof(bool),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public string AxisLabelX
    {
        get => (string)GetValue(AxisLabelXProperty);
        set => SetValue(AxisLabelXProperty, value);
    }

    public static readonly DependencyProperty AxisLabelXProperty =
        DependencyProperty.Register(
            nameof(AxisLabelX),
            typeof(string),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public string AxisLabelY
    {
        get => (string)GetValue(AxisLabelYProperty);
        set => SetValue(AxisLabelYProperty, value);
    }

    public static readonly DependencyProperty AxisLabelYProperty =
        DependencyProperty.Register(
            nameof(AxisLabelY),
            typeof(string),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public string ChartLabel
    {
        get => (string)GetValue(ChartLabelProperty);
        set => SetValue(ChartLabelProperty, value);
    }

    public static readonly DependencyProperty ChartLabelProperty =
        DependencyProperty.Register(
            nameof(ChartLabel),
            typeof(string),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public string AxisUnitX
    {
        get => (string)GetValue(AxisUnitXProperty);
        set => SetValue(AxisUnitXProperty, value);
    }

    public static readonly DependencyProperty AxisUnitXProperty =
        DependencyProperty.Register(
            nameof(AxisUnitX),
            typeof(string),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public string AxisUnitY
    {
        get => (string)GetValue(AxisUnitYProperty);
        set => SetValue(AxisUnitYProperty, value);
    }

    public static readonly DependencyProperty AxisUnitYProperty =
        DependencyProperty.Register(
            nameof(AxisUnitY),
            typeof(string),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public int AxisLabelPrecisionX
    {
        get => (int)GetValue(AxisLabelPrecisionXProperty);
        set => SetValue(AxisLabelPrecisionXProperty, Math.Clamp(value, 0, 10));
    }

    public static readonly DependencyProperty AxisLabelPrecisionXProperty =
        DependencyProperty.Register(
            nameof(AxisLabelPrecisionX),
            typeof(int),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsRender));

    public int AxisLabelPrecisionY
    {
        get => (int)GetValue(AxisLabelPrecisionYProperty);
        set => SetValue(AxisLabelPrecisionYProperty, Math.Clamp(value, 0, 10));
    }

    public static readonly DependencyProperty AxisLabelPrecisionYProperty =
        DependencyProperty.Register(
            nameof(AxisLabelPrecisionY),
            typeof(int),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsRender));

    public string AxisLabelFormatX
    {
        get => (string)GetValue(AxisLabelFormatXProperty);
        set => SetValue(AxisLabelFormatXProperty, value);
    }

    public static readonly DependencyProperty AxisLabelFormatXProperty =
        DependencyProperty.Register(
            nameof(AxisLabelFormatX),
            typeof(string),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public string AxisLabelFormatY
    {
        get => (string)GetValue(AxisLabelFormatYProperty);
        set => SetValue(AxisLabelFormatYProperty, value);
    }

    public static readonly DependencyProperty AxisLabelFormatYProperty =
        DependencyProperty.Register(
            nameof(AxisLabelFormatY),
            typeof(string),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public double AxisFontSize
    {
        get => (double)GetValue(AxisFontSizeProperty);
        set => SetValue(AxisFontSizeProperty, value);
    }

    public static readonly DependencyProperty AxisFontSizeProperty =
        DependencyProperty.Register(
            nameof(AxisFontSize),
            typeof(double),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(15d, FrameworkPropertyMetadataOptions.AffectsRender, OnUiScaleChanged));

    public double TitleFontSize
    {
        get => (double)GetValue(TitleFontSizeProperty);
        set => SetValue(TitleFontSizeProperty, value);
    }

    public static readonly DependencyProperty TitleFontSizeProperty =
        DependencyProperty.Register(
            nameof(TitleFontSize),
            typeof(double),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(16.5d, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool ShowXAxis
    {
        get => (bool)GetValue(ShowXAxisProperty);
        set => SetValue(ShowXAxisProperty, value);
    }

    public static readonly DependencyProperty ShowXAxisProperty =
        DependencyProperty.Register(
            nameof(ShowXAxis),
            typeof(bool),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool ShowYAxis
    {
        get => (bool)GetValue(ShowYAxisProperty);
        set => SetValue(ShowYAxisProperty, value);
    }

    public static readonly DependencyProperty ShowYAxisProperty =
        DependencyProperty.Register(
            nameof(ShowYAxis),
            typeof(bool),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Multiplier from host grid zoom (Ctrl+wheel); scales strokes and axis typography.</summary>
    public double UiScale
    {
        get => (double)GetValue(UiScaleProperty);
        set => SetValue(UiScaleProperty, value);
    }

    public static readonly DependencyProperty UiScaleProperty =
        DependencyProperty.Register(
            nameof(UiScale),
            typeof(double),
            typeof(SkiaChartBaseControl),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender, OnUiScaleChanged));

    private static void OnUiScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SkiaChartBaseControl)d).ApplyUiScaleToResources();
    }

    /// <summary>Clamped UI scale for pixel geometry (padding, strokes, fonts).</summary>
    protected float PlotUiScale => (float)Math.Clamp(UiScale, 0.25, 4.0);

    protected bool IsSparkline => ActualWidth < SparklineWidthThreshold || ActualHeight < SparklineHeightThreshold;

    /// <summary>Minimal insets and no axis ticks when the control is small; same in <see cref="ChartRenderMode.Editor"/> and <see cref="ChartRenderMode.Renderer"/>.</summary>
    protected bool UseSparklineLayout => IsSparkline;

    protected SKRect PlotRect => _coordinates.PlotRect;

    /// <summary>
    /// Call after the host has finished sizing the chart (e.g. grid cell layout). Clears the hit-test sync so the next
    /// <see cref="ToChartPoint"/> recomputes plot geometry from current <see cref="ActualWidth"/> / <see cref="ActualHeight"/>.
    /// </summary>
    public void ResetHitTestGeometrySync() => _coordinates.ResetHitTestGeometrySync();

    /// <summary>Maps a mouse position in WPF DIP space to chart data coordinates (handles DPI vs Skia surface pixels).</summary>
    protected ChartPoint ToChartPoint(Point logicalPosition)
    {
        SyncHitTestGeometryFromLayout();
        var surface = _coordinates.LogicalPointToSurface(logicalPosition, ActualWidth, ActualHeight);
        return _coordinates.SurfacePixelToChartPoint(surface);
    }

    /// <summary>
    /// Plot rect and surface pixel size are normally updated in <see cref="OnPaintSurface"/>.
    /// When the chart is reparented or resized, hit-testing can run before the next paint — sync from layout first.
    /// </summary>
    private void SyncHitTestGeometryFromLayout()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        _coordinates.EnsureHitTestGeometryFromLayout(
            ActualWidth,
            ActualHeight,
            dpi.PixelsPerDip,
            UseSparklineLayout,
            PlotUiScale,
            ShowXAxis,
            ShowYAxis,
            AxisFontSize,
            ShowYAxis && !string.IsNullOrWhiteSpace(AxisLabelY),
            ShowXAxis && !string.IsNullOrWhiteSpace(AxisLabelX));
    }

    private double SurfaceScaleX() => ActualWidth > 0 ? _coordinates.SurfacePixelWidth / ActualWidth : 1d;

    private double SurfaceScaleY() => ActualHeight > 0 ? _coordinates.SurfacePixelHeight / ActualHeight : 1d;

    protected void RequestRender()
    {
        InvalidateVisual();
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (SkiaChartBaseControl)d;
        if (chart.SuppressAutomaticViewportFitOnNextPointsChange)
        {
            chart.SuppressAutomaticViewportFitOnNextPointsChange = false;
            if (chart.Points.Count > 0)
            {
                chart.InvalidateVisual();
                return;
            }
        }

        chart.UpdateViewportFromData();
        chart.InvalidateVisual();
    }
}
