using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using Plotto.Charting.Core;

namespace Plotto.Charting.Controls;

public abstract class SkiaChartBaseControl : SKElement
{
    private const float CellPadding = 4f;
    private const float AxisReserveX = 36f;
    private const float AxisReserveY = 18f;
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
    private int _surfacePixelWidth = 1;
    private int _surfacePixelHeight = 1;
    private Point _lastMousePos;
    private bool _isPanning;
    private bool _isDragging;
    private bool _isRightDragZoom;
    private Point _zoomRectStart;
    private Point _zoomRectCurrent;
    private SKRect _plotRect;

    /// <summary>Last <see cref="ActualWidth"/>/<see cref="ActualHeight"/> used to build hit-test geometry; NaN forces a refresh.</summary>
    private double _hitTestSyncedActualWidth = double.NaN;
    private double _hitTestSyncedActualHeight = double.NaN;

    /// <summary>Set only while <see cref="ApplyRightDragZoom"/> invokes <see cref="ApplyViewportInteractionClamp"/>.</summary>
    private bool _viewportClampAfterRectZoom;

    /// <summary>Calibration (and other overrides) can preserve rubber-band X/Y when true; wheel/pan use full clamp.</summary>
    protected bool IsViewportClampAfterRectZoom => _viewportClampAfterRectZoom;

    /// <summary>Right-drag zoom box: capture only after movement so a plain click is not a drag zoom.</summary>
    private bool _rightZoomRubberPending;

    /// <summary>Deferred open so double right-click can reach the chart before the menu popup steals focus.</summary>
    private DispatcherTimer? _deferredContextMenuTimer;

    private Point _deferredContextMenuPosition;

    /// <summary>Slightly above typical OS double-click time so the second click is recognized before the menu opens.</summary>
    private const int DeferredContextMenuDelayMs = 450;

    /// <summary>Wheel zoom / pan X limits: plot xmin/xmax ± 5% of (xmax−xmin).</summary>
    private double _zoomClampXMin;
    private double _zoomClampXMax;

    protected SkiaChartBaseControl()
    {
        Focusable = true;
        IsHitTestVisible = true;
        SnapsToDevicePixels = true;
        ApplyUiScaleToResources();
    }

    protected virtual void ApplyUiScaleToResources()
    {
        var s = PlotUiScale;
        _linePaint.StrokeWidth = 2f * s;
        AxisStrokePaint.StrokeWidth = Math.Max(0.5f, 1f * s);
        AxisFont.Size = 10f * s;
        _overlayStroke.StrokeWidth = Math.Max(0.5f, 1f * s);
        _zoomRubberStrokePaint.StrokeWidth = Math.Max(0.5f, 1f * s);
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

    protected SKRect PlotRect => _plotRect;

    /// <summary>Renders the current chart to SVG markup (same drawing path as on-screen).</summary>
    public string GetChartSvgMarkup(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        using var stream = new MemoryStream();
        using (var canvas = SKSvgCanvas.Create(new SKRect(0, 0, width, height), stream))
        {
            DrawChart(canvas, width, height);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Raster snapshot for clipboard HTML (Excel-friendly vs inline SVG).</summary>
    public byte[] GetChartPngBytes(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        DrawChart(surface.Canvas, width, height);
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    /// <summary>Removes the XML prologue so SVG can be inlined in HTML.</summary>
    public static string TrimSvgXmlDeclaration(string svg)
    {
        if (string.IsNullOrEmpty(svg))
        {
            return svg;
        }

        var s = svg.AsSpan().TrimStart();
        if (s.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
        {
            var idx = svg.IndexOf("?>", StringComparison.Ordinal);
            if (idx >= 0)
            {
                return svg[(idx + 2)..].TrimStart();
            }
        }

        return svg;
    }

    public void CopyAsSvgToClipboard()
    {
        var width = (int)Math.Max(1, ActualWidth);
        var height = (int)Math.Max(1, ActualHeight);
        var svg = GetChartSvgMarkup(width, height);
        var dataObject = new DataObject();
        dataObject.SetData("image/svg+xml", svg);
        dataObject.SetData(DataFormats.UnicodeText, svg);
        Clipboard.SetDataObject(dataObject, true);
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);
        DrawChart(e.Surface.Canvas, e.Info.Width, e.Info.Height);
        if (ActualWidth > 0 && ActualHeight > 0)
        {
            _hitTestSyncedActualWidth = ActualWidth;
            _hitTestSyncedActualHeight = ActualHeight;
        }
    }

    /// <summary>
    /// Call after the host has finished sizing the chart (e.g. grid cell layout). Clears the hit-test sync so the next
    /// <see cref="ToChartPoint"/> recomputes <see cref="_plotRect"/> from current <see cref="ActualWidth"/> / <see cref="ActualHeight"/>.
    /// </summary>
    public void ResetHitTestGeometrySync()
    {
        _hitTestSyncedActualWidth = double.NaN;
        _hitTestSyncedActualHeight = double.NaN;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (ApplyWheelZoomFromRoute(e))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Wheel zoom using <see cref="MouseWheelEventArgs.GetPosition(UIElement)"/> relative to this control.
    /// Used when a parent (e.g. data grid) handles <see cref="UIElement.PreviewMouseWheel"/> and routes here.
    /// </summary>
    public virtual bool ApplyWheelZoomFromRoute(MouseWheelEventArgs e)
    {
        if (!CanUseScrollWheelZoom())
        {
            return false;
        }

        ProcessWheelZoom(e.GetPosition(this), e.Delta);
        return true;
    }

    private void ProcessWheelZoom(Point pivot, int delta)
    {
        var factor = delta > 0 ? 0.9 : 1.1;
        var mod = Keyboard.Modifiers;
        var ctrlWithoutShift = mod.HasFlag(ModifierKeys.Control) && !mod.HasFlag(ModifierKeys.Shift);
        if (ctrlWithoutShift || IsPointerOverXAxisScrollZone(pivot))
        {
            ZoomXAt(pivot, factor);
        }
        else
        {
            ZoomYAt(pivot, factor);
        }
    }

    /// <summary>
    /// Hit-test the horizontal axis band (below the plot) in surface pixels, matching <see cref="DrawChart"/> layout.
    /// </summary>
    private bool IsPointerOverXAxisScrollZone(Point logicalPosition)
    {
        var w = _surfacePixelWidth;
        var h = _surfacePixelHeight;
        if (w <= 2 || h <= 2)
        {
            return false;
        }

        var surf = LogicalPointToSurface(logicalPosition);
        var sx = (float)surf.X;
        var sy = (float)surf.Y;

        var zs = PlotUiScale;
        var pad = CellPadding * zs;
        var ax = AxisReserveX * zs;
        var ay = AxisReserveY * zs;
        if (UseSparklineLayout)
        {
            var plotLeft = pad;
            var plotRight = w - pad;
            var bandTop = h - pad - ay;
            var bandBottom = h - pad;
            return sy >= bandTop && sy <= bandBottom && sx >= plotLeft && sx <= plotRight;
        }

        var plotRect = new SKRect(pad + ax, pad, w - pad, h - pad - ay);
        return sy >= plotRect.Bottom && sy <= h - pad && sx >= plotRect.Left && sx <= plotRect.Right;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.ChangedButton == MouseButton.Right && e.ClickCount == 2)
        {
            CancelDeferredContextMenu();

            if (ContextMenu is { } menu)
            {
                menu.IsOpen = false;
            }

            Focus();
            FitViewportToAllData();
            _rightZoomRubberPending = false;
            _isRightDragZoom = false;
            _isDragging = false;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            e.Handled = true;
            return;
        }

        if (!CanInteract())
        {
            return;
        }

        Focus();
        if (EnablePopupEditing && UseSparklineLayout && e.ClickCount == 2)
        {
            PopupEditorRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        _lastMousePos = e.GetPosition(this);

        if (e.ChangedButton == MouseButton.Right)
        {
            CancelDeferredContextMenu();

            _rightZoomRubberPending = true;
            _zoomRectStart = LogicalPointToSurface(_lastMousePos);
            _zoomRectCurrent = _zoomRectStart;
            CaptureMouse();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Middle)
        {
            CaptureMouse();
            _isDragging = true;
            _isPanning = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        CaptureMouse();
        _isDragging = true;
        OnChartMouseDown(ToChartPoint(_lastMousePos), e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pos = e.GetPosition(this);
        var canInteract = CanInteract();

        if (_rightZoomRubberPending && canInteract && !_isRightDragZoom)
        {
            var surf = LogicalPointToSurface(pos);
            var ox = surf.X - _zoomRectStart.X;
            var oy = surf.Y - _zoomRectStart.Y;
            if (ox * ox + oy * oy >= 16f)
            {
                CaptureMouse();
                _isDragging = true;
                _isRightDragZoom = true;
                _zoomRectCurrent = surf;
                InvalidateVisual();
            }
        }

        if (_isDragging)
        {
            if (_isRightDragZoom)
            {
                if (canInteract)
                {
                    _zoomRectCurrent = LogicalPointToSurface(pos);
                    InvalidateVisual();
                }
            }
            else if (_isPanning)
            {
                if (canInteract)
                {
                    var sx = (pos.X - _lastMousePos.X) * SurfaceScaleX();
                    var sy = (pos.Y - _lastMousePos.Y) * SurfaceScaleY();
                    PanByPixels(sx, sy);
                }
            }
            else
            {
                // Left-button chart gesture (e.g. manual integration).
                // Use _isDragging (set on mouse down, cleared on mouse up), not e.LeftButton:
                // SKElement/WPF sometimes reports Released on MouseMove during capture, which blocks all drags.
                OnChartMouseDrag(ToChartPoint(pos), e);
                InvalidateVisual();
            }
        }

        _lastMousePos = pos;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        var pos = e.GetPosition(this);
        var canInteract = CanInteract();

        if (e.ChangedButton == MouseButton.Right)
        {
            var rubberWasPending = _rightZoomRubberPending;
            if (canInteract && _isRightDragZoom)
            {
                SyncZoomClampBoundsFromPoints();
                ApplyRightDragZoom(_zoomRectStart, _zoomRectCurrent);
            }
            else if (canInteract && rubberWasPending && !_isRightDragZoom)
            {
                // Same place as mouse down (no drag-zoom threshold): editor context menu; otherwise rubber-band zoom on mouse up.
                TryOpenEditorContextMenu(e);
            }

            _rightZoomRubberPending = false;
            _isRightDragZoom = false;
            _isDragging = false;
            ReleaseMouseCapture();
            InvalidateVisual();
            e.Handled = canInteract;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && _isDragging && !_isPanning && !_isRightDragZoom)
        {
            var chartPoint = ToChartPoint(pos);
            if (canInteract)
            {
                OnChartMouseUp(chartPoint, e);
                DataPointClicked?.Invoke(this, new ChartPointEventArgs(chartPoint));
            }
            else
            {
                // Finish chart gesture and release capture even if interaction flags flipped (avoids stuck drag).
                OnChartMouseUp(chartPoint, e);
            }
        }

        if (canInteract && e.ChangedButton == MouseButton.Middle && _isPanning)
        {
            _isPanning = false;
        }

        _isDragging = false;
        ReleaseMouseCapture();
    }

    /// <summary>
    /// Schedules <see cref="FrameworkElement.ContextMenu"/> after a short delay so a fast second click can register as double-click on this element.
    /// </summary>
    protected virtual bool TryOpenEditorContextMenu(MouseButtonEventArgs e)
    {
        if (ContextMenu is null)
        {
            return false;
        }

        ScheduleDeferredContextMenu(e.GetPosition(this));
        return true;
    }

    private void CancelDeferredContextMenu()
    {
        if (_deferredContextMenuTimer is null)
        {
            return;
        }

        _deferredContextMenuTimer.Stop();
        _deferredContextMenuTimer.Tick -= OnDeferredContextMenuTick;
        _deferredContextMenuTimer = null;
    }

    private void ScheduleDeferredContextMenu(Point positionInChart)
    {
        CancelDeferredContextMenu();
        _deferredContextMenuPosition = positionInChart;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DeferredContextMenuDelayMs) };
        timer.Tick += OnDeferredContextMenuTick;
        _deferredContextMenuTimer = timer;
        timer.Start();
    }

    private void OnDeferredContextMenuTick(object? sender, EventArgs e)
    {
        CancelDeferredContextMenu();
        OpenDeferredEditorContextMenu();
    }

    private void OpenDeferredEditorContextMenu()
    {
        var menu = ContextMenu;
        if (menu is null)
        {
            return;
        }

        menu.Focusable = false;
        menu.PlacementTarget = this;
        menu.Placement = PlacementMode.RelativePoint;
        menu.HorizontalOffset = _deferredContextMenuPosition.X;
        menu.VerticalOffset = _deferredContextMenuPosition.Y;
        menu.IsOpen = true;
    }

    protected virtual void OnChartMouseDown(ChartPoint point, MouseButtonEventArgs e)
    {
    }

    protected virtual void OnChartMouseDrag(ChartPoint point, MouseEventArgs e)
    {
    }

    protected virtual void OnChartMouseUp(ChartPoint point, MouseButtonEventArgs e)
    {
    }

    protected virtual void DrawSeries(SKCanvas canvas, IReadOnlyList<ChartPoint> points, SKRect plotRect)
    {
        if (points.Count == 0)
        {
            return;
        }

        var sampled = points.Count > 500 ? Downsample(points, Math.Max(200, (int)plotRect.Width)) : points;
        var builder = new SKPathBuilder();
        var first = sampled[0];
        builder.MoveTo(ToPixelX(first.X, plotRect), ToPixelY(first.Y, plotRect));
        for (var i = 1; i < sampled.Count; i++)
        {
            var p = sampled[i];
            builder.LineTo(ToPixelX(p.X, plotRect), ToPixelY(p.Y, plotRect));
        }

        using var path = builder.Detach();
        canvas.DrawPath(path, _linePaint);
    }

    protected virtual void DrawOverlay(SKCanvas canvas, SKRect plotRect)
    {
    }

    protected virtual bool CanInteract()
    {
        return EnableMouseInteractions && EnableInlineEditing && RenderMode == ChartRenderMode.Editor;
    }

    /// <summary>
    /// Wheel zoom only in <see cref="ChartRenderMode.Editor"/> (inline chart editor). In
    /// <see cref="ChartRenderMode.Renderer"/> the wheel is left for the parent grid to scroll.
    /// </summary>
    protected virtual bool CanUseScrollWheelZoom()
    {
        if (!EnableMouseInteractions)
        {
            return false;
        }

        return RenderMode == ChartRenderMode.Editor;
    }

    protected float ToPixelX(double x, SKRect plotRect)
    {
        return plotRect.Left + (float)((x - Viewport.XMin) / (Viewport.XMax - Viewport.XMin) * plotRect.Width);
    }

    protected float ToPixelY(double y, SKRect plotRect)
    {
        return plotRect.Bottom - (float)((y - Viewport.YMin) / (Viewport.YMax - Viewport.YMin) * plotRect.Height);
    }

    /// <summary>Non-negative axis tick values only; negative extrema are not labelled.</summary>
    protected static bool ShouldDrawAxisLabel(double value, double tolerance = 1e-12)
    {
        return value >= -tolerance;
    }

    /// <summary>Maps a mouse position in WPF DIP space to chart data coordinates (handles DPI vs Skia surface pixels).</summary>
    protected ChartPoint ToChartPoint(Point logicalPosition)
    {
        EnsureHitTestGeometryFromLayout();
        return DataPointFromSurfacePixel(LogicalPointToSurface(logicalPosition));
    }

    /// <summary>
    /// <see cref="_plotRect"/> and surface pixel size are normally updated in <see cref="OnPaintSurface"/>.
    /// When the chart is reparented or resized, hit-testing (e.g. relayed mouse down from a host grid) can run
    /// before the next paint — then stale geometry maps clicks to the wrong data X/Y. Sync from layout first.
    /// </summary>
    private void EnsureHitTestGeometryFromLayout()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        if (!double.IsNaN(_hitTestSyncedActualWidth)
            && ActualWidth == _hitTestSyncedActualWidth
            && ActualHeight == _hitTestSyncedActualHeight)
        {
            return;
        }

        _hitTestSyncedActualWidth = ActualWidth;
        _hitTestSyncedActualHeight = ActualHeight;
        var dpi = VisualTreeHelper.GetDpi(this);
        var pw = (int)Math.Max(1, Math.Round(ActualWidth * dpi.PixelsPerDip));
        var ph = (int)Math.Max(1, Math.Round(ActualHeight * dpi.PixelsPerDip));
        UpdatePlotRectFromSurfaceSize(pw, ph);
    }

    /// <summary>Aligns Skia pixel size and <see cref="_plotRect"/> with the framebuffer dimensions used for drawing.</summary>
    private void UpdatePlotRectFromSurfaceSize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        _surfacePixelWidth = width;
        _surfacePixelHeight = height;

        var s = PlotUiScale;
        var pad = CellPadding * s;
        var ax = AxisReserveX * s;
        var ay = AxisReserveY * s;
        if (UseSparklineLayout)
        {
            _plotRect = new SKRect(pad, pad, width - pad, height - pad);
        }
        else
        {
            _plotRect = new SKRect(pad + ax, pad, width - pad, height - pad - ay);
        }
    }

    private Point LogicalPointToSurface(Point logical)
    {
        var aw = ActualWidth;
        var ah = ActualHeight;
        if (aw <= 0 || ah <= 0)
        {
            return logical;
        }

        return new Point(
            logical.X * _surfacePixelWidth / aw,
            logical.Y * _surfacePixelHeight / ah);
    }

    private double SurfaceScaleX() => ActualWidth > 0 ? _surfacePixelWidth / ActualWidth : 1d;

    private double SurfaceScaleY() => ActualHeight > 0 ? _surfacePixelHeight / ActualHeight : 1d;

    /// <summary>Maps a point in the same pixel space as <see cref="_plotRect"/> (Skia surface / framebuffer).</summary>
    private ChartPoint DataPointFromSurfacePixel(Point surfacePixel)
    {
        var x = Viewport.XMin + ((surfacePixel.X - _plotRect.Left) / Math.Max(1, _plotRect.Width)) * (Viewport.XMax - Viewport.XMin);
        var y = Viewport.YMin + ((_plotRect.Bottom - surfacePixel.Y) / Math.Max(1, _plotRect.Height)) * (Viewport.YMax - Viewport.YMin);
        return new ChartPoint(x, y);
    }

    protected void RequestRender()
    {
        InvalidateVisual();
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (SkiaChartBaseControl)d;
        chart.UpdateViewportFromData();
        chart.InvalidateVisual();
    }

    private void DrawChart(SKCanvas canvas, int width, int height)
    {
        canvas.Clear(SKColors.Transparent);

        if (width <= 0 || height <= 0)
        {
            return;
        }

        UpdatePlotRectFromSurfaceSize(width, height);

        if (_plotRect.Width <= 2 || _plotRect.Height <= 2)
        {
            return;
        }

        if (!Viewport.IsValid || Points.Count == 0)
        {
            UpdateViewportFromData();
        }

        canvas.Save();
        canvas.ClipRect(_plotRect);

        if (Points.Count > 0)
        {
            DrawSeries(canvas, Points, _plotRect);
        }

        DrawOverlay(canvas, _plotRect);

        canvas.Restore();

        if (!UseSparklineLayout)
        {
            DrawAxes(canvas, _plotRect);
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
        var axOff = 4f * zs;
        var below = 14f * zs;
        var topLab = 10f * zs;
        canvas.DrawLine(plotRect.Left, plotRect.Bottom, plotRect.Right, plotRect.Bottom, AxisStrokePaint);
        canvas.DrawLine(plotRect.Left, plotRect.Top, plotRect.Left, plotRect.Bottom, AxisStrokePaint);
        if (ShouldDrawAxisLabel(Viewport.XMin))
        {
            canvas.DrawText($"{Viewport.XMin:0.##}", plotRect.Left, plotRect.Bottom + below, SKTextAlign.Left, AxisFont, AxisLabelPaint);
        }

        if (ShouldDrawAxisLabel(Viewport.XMax))
        {
            canvas.DrawText($"{Viewport.XMax:0.##}", plotRect.Right, plotRect.Bottom + below, SKTextAlign.Right, AxisFont, AxisLabelPaint);
        }

        if (ShouldDrawAxisLabel(Viewport.YMax))
        {
            canvas.DrawText($"{Viewport.YMax:0.##}", plotRect.Left - axOff, plotRect.Top + topLab, SKTextAlign.Right, AxisFont, AxisLabelPaint);
        }

        if (ShouldDrawAxisLabel(Viewport.YMin))
        {
            canvas.DrawText($"{Viewport.YMin:0.##}", plotRect.Left - axOff, plotRect.Bottom, SKTextAlign.Right, AxisFont, AxisLabelPaint);
        }
    }

    private void UpdateViewportFromData()
    {
        var points = Points;
        if (points.Count == 0)
        {
            Viewport.XMin = 0;
            Viewport.XMax = 1;
            Viewport.YMin = 0;
            Viewport.YMax = 1;
            _zoomClampXMin = 0;
            _zoomClampXMax = 1;
            return;
        }

        GetPlotPointMinMax(points, out var xmin, out var xmax, out var ymin, out var ymax);
        var dx = xmax - xmin;
        var dy = ymax - ymin;
        SyncZoomClampBoundsForPlotExtents(xmin, xmax, dx);

        var xMargin = Math.Max(1e-6, dx * 0.02);
        var yMargin = Math.Max(1e-6, dy * 0.05);
        Viewport.XMin = xmin - xMargin;
        Viewport.XMax = xmax + xMargin;
        Viewport.YMin = ymin - yMargin;
        Viewport.YMax = ymax + yMargin;
        Viewport.EnsureMinimumSize();
        ApplyViewportInteractionClamp();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>After wheel zoom, pan, or drag-zoom; default uses point-based X/Y clamps.</summary>
    protected virtual void ApplyViewportInteractionClamp()
    {
        ClampViewportToWheelZoomLimits();
    }

    /// <summary>
    /// xmin/xmax/ymin/ymax are the min and max coordinates among <see cref="Points"/> only (not the viewport).
    /// </summary>
    protected static void GetPlotPointMinMax(IReadOnlyList<ChartPoint> points, out double xmin, out double xmax, out double ymin, out double ymax)
    {
        xmin = points[0].X;
        xmax = points[0].X;
        ymin = points[0].Y;
        ymax = points[0].Y;
        for (var i = 1; i < points.Count; i++)
        {
            var p = points[i];
            if (p.X < xmin) xmin = p.X;
            if (p.X > xmax) xmax = p.X;
            if (p.Y < ymin) ymin = p.Y;
            if (p.Y > ymax) ymax = p.Y;
        }
    }

    private void SyncZoomClampBoundsFromPoints()
    {
        var points = Points;
        if (points.Count == 0)
        {
            return;
        }

        GetPlotPointMinMax(points, out var xmin, out var xmax, out _, out _);
        var dx = xmax - xmin;
        SyncZoomClampBoundsForPlotExtents(xmin, xmax, dx);
    }

    /// <summary>
    /// X-only clamp box: plot xmin/xmax ± 5% of (xmax−xmin). Y uses <see cref="ClampViewportYUsingVisibleChartHeight"/>.
    /// </summary>
    private void SyncZoomClampBoundsForPlotExtents(double xmin, double xmax, double dx)
    {
        var padX = Math.Max(1e-12, dx * 0.05);
        _zoomClampXMin = xmin - padX;
        _zoomClampXMax = xmax + padX;
    }

    /// <summary>
    /// X: viewport inside plot xmin/xmax ± 5% of horizontal data span.
    /// Y: ymin/ymax are plot-point extrema; chart height = visible span (Viewport.YMax − Viewport.YMin).
    /// Keeps YMin ≥ ymin − 5%·chartHeight and YMax ≤ ymax + 5%·chartHeight (same chart height h).
    /// </summary>
    private void ClampViewportToWheelZoomLimits()
    {
        if (Points.Count == 0)
        {
            return;
        }

        SyncZoomClampBoundsFromPoints();

        GetPlotPointMinMax(Points, out _, out _, out var yminPlot, out var ymaxPlot);

        var limW = _zoomClampXMax - _zoomClampXMin;
        var w = Viewport.XMax - Viewport.XMin;
        const double eps = 1e-12;

        if (w >= limW - eps)
        {
            Viewport.XMin = _zoomClampXMin;
            Viewport.XMax = _zoomClampXMax;
        }
        else
        {
            var maxXMin = _zoomClampXMax - w;
            Viewport.XMin = Math.Clamp(Viewport.XMin, _zoomClampXMin, maxXMin);
            Viewport.XMax = Viewport.XMin + w;
        }

        ClampViewportYUsingVisibleChartHeight(yminPlot, ymaxPlot);
        Viewport.EnsureMinimumSize();
        ClampViewportYUsingVisibleChartHeight(yminPlot, ymaxPlot);
    }

    /// <summary>
    /// yminPlot/ymaxPlot are min/max Y over plot points. Chart height h = visible Y span in data coordinates.
    /// Enforces the bottom visibility rule: YMin is never below yminPlot − 0.05·h.
    /// </summary>
    private void ClampViewportYUsingVisibleChartHeight(double yminPlot, double ymaxPlot)
    {
        const double eps = 1e-12;
        var h = Viewport.YMax - Viewport.YMin;
        if (h <= eps)
        {
            return;
        }

        // Keep a fixed 5% bottom margin so the lowest point stays above the axis.
        // y-axis floor follows the lowest data point with a 5% chart-height offset.
        var clampedYMin = yminPlot - (0.05 * h);
        if (Math.Abs(clampedYMin - Viewport.YMin) > eps)
        {
            Viewport.YMin = clampedYMin;
            Viewport.YMax = Viewport.YMin + h;
        }
    }

    protected void ZoomXAt(Point pivot, double scale)
    {
        var pivotData = ToChartPoint(pivot);
        var xRange = (Viewport.XMax - Viewport.XMin) * scale;
        Viewport.XMin = pivotData.X - (pivotData.X - Viewport.XMin) * scale;
        Viewport.XMax = Viewport.XMin + xRange;
        ApplyViewportInteractionClamp();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    protected void ZoomYAt(Point pivot, double scale)
    {
        var pivotData = ToChartPoint(pivot);
        var yRange = (Viewport.YMax - Viewport.YMin) * scale;
        Viewport.YMin = pivotData.Y - (pivotData.Y - Viewport.YMin) * scale;
        Viewport.YMax = Viewport.YMin + yRange;
        ApplyViewportInteractionClamp();
        OnAfterYZoom(scale);
        ViewportChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>
    /// Called after <see cref="ApplyViewportInteractionClamp"/> from <see cref="ZoomYAt"/> only.
    /// <paramref name="scale"/> &gt; 1 zooms out (larger Y span); &lt; 1 zooms in.
    /// </summary>
    protected virtual void OnAfterYZoom(double scale)
    {
    }

    private void ApplyRightDragZoom(Point a, Point b)
    {
        if (_plotRect.Width <= 1 || _plotRect.Height <= 1)
        {
            return;
        }

        var x0 = Math.Clamp(Math.Min(a.X, b.X), _plotRect.Left, _plotRect.Right);
        var x1 = Math.Clamp(Math.Max(a.X, b.X), _plotRect.Left, _plotRect.Right);
        var y0 = Math.Clamp(Math.Min(a.Y, b.Y), _plotRect.Top, _plotRect.Bottom);
        var y1 = Math.Clamp(Math.Max(a.Y, b.Y), _plotRect.Top, _plotRect.Bottom);
        if (x1 - x0 < 4 || y1 - y0 < 4)
        {
            return;
        }

        var bottomLeft = DataPointFromSurfacePixel(new Point(x0, y1));
        var topRight = DataPointFromSurfacePixel(new Point(x1, y0));
        Viewport.XMin = Math.Min(bottomLeft.X, topRight.X);
        Viewport.XMax = Math.Max(bottomLeft.X, topRight.X);
        Viewport.YMin = Math.Min(bottomLeft.Y, topRight.Y);
        Viewport.YMax = Math.Max(bottomLeft.Y, topRight.Y);
        _viewportClampAfterRectZoom = true;
        try
        {
            ApplyViewportInteractionClamp();
        }
        finally
        {
            _viewportClampAfterRectZoom = false;
        }

        ViewportChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void PanByPixels(double dx, double dy)
    {
        var xPerPixel = (Viewport.XMax - Viewport.XMin) / Math.Max(1d, _plotRect.Width);
        var yPerPixel = (Viewport.YMax - Viewport.YMin) / Math.Max(1d, _plotRect.Height);
        var xDelta = dx * xPerPixel;
        var yDelta = dy * yPerPixel;

        Viewport.XMin -= xDelta;
        Viewport.XMax -= xDelta;
        Viewport.YMin += yDelta;
        Viewport.YMax += yDelta;
        ApplyViewportInteractionClamp();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>
    /// Fits the viewport to all data (full zoom out). Requires edit mode — use double right-click on the chart for reset while viewing in renderer mode.
    /// </summary>
    public void ZoomOutCompletely()
    {
        if (!CanInteract())
        {
            return;
        }

        FitViewportToAllData();
    }

    private void FitViewportToAllData()
    {
        UpdateViewportFromData();
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (!CanInteract())
        {
            return;
        }

        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.None)
        {
            ZoomOutCompletely();
            e.Handled = true;
        }
    }

    private static IReadOnlyList<ChartPoint> Downsample(IReadOnlyList<ChartPoint> points, int targetCount)
    {
        if (points.Count <= targetCount)
        {
            return points;
        }

        var step = (double)points.Count / targetCount;
        var list = new List<ChartPoint>(targetCount);
        for (var i = 0d; i < points.Count && list.Count < targetCount; i += step)
        {
            list.Add(points[(int)i]);
        }

        if (list.Count == 0 || list[^1] != points[^1])
        {
            list.Add(points[^1]);
        }

        return list;
    }

    protected void DrawFilledXRange(SKCanvas canvas, SKRect plotRect, double fromX, double toX)
    {
        var x1 = ToPixelX(fromX, plotRect);
        var x2 = ToPixelX(toX, plotRect);
        var left = Math.Min(x1, x2);
        var right = Math.Max(x1, x2);
        var rect = new SKRect(left, plotRect.Top, right, plotRect.Bottom);
        canvas.DrawRect(rect, _overlayFill);
        canvas.DrawRect(rect, _overlayStroke);
    }
}
