using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Plotto.Charting.Core;
using Plotto.Charting.Geometry;
using SkiaSharp;

namespace Plotto.Charting.Controls;

public abstract partial class SkiaChartBaseControl
{
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
        SyncHitTestGeometryFromLayout();
        var w = _coordinates.SurfacePixelWidth;
        var h = _coordinates.SurfacePixelHeight;
        if (w <= 2 || h <= 2)
        {
            return false;
        }

        var surf = _coordinates.LogicalPointToSurface(logicalPosition, ActualWidth, ActualHeight);
        var sx = (float)surf.X;
        var sy = (float)surf.Y;

        var zs = PlotUiScale;
        var pad = ChartPlotLayout.CellPadding * zs;
        var ax = ChartPlotLayout.AxisReserveX * zs;
        var ay = ChartPlotLayout.AxisReserveY * zs;
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
            SyncHitTestGeometryFromLayout();
            _zoomRectStart = _coordinates.LogicalPointToSurface(_lastMousePos, ActualWidth, ActualHeight);
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
            SyncHitTestGeometryFromLayout();
            var surf = _coordinates.LogicalPointToSurface(pos, ActualWidth, ActualHeight);
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
                    SyncHitTestGeometryFromLayout();
                    _zoomRectCurrent = _coordinates.LogicalPointToSurface(pos, ActualWidth, ActualHeight);
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
                _viewportWheelClamp.ResyncXClampFromPoints(Points);
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

    private void CancelDeferredContextMenu() => _deferredContextMenu.Cancel();

    private void ScheduleDeferredContextMenu(Point positionInChart)
    {
        _deferredContextMenu.Cancel();
        _deferredContextMenuPosition = positionInChart;
        _deferredContextMenu.Schedule(Dispatcher, DeferredContextMenuDelayMs, OpenDeferredEditorContextMenu);
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
}
