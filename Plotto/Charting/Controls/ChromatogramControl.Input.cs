using System.Windows;
using System.Windows.Input;
using Plotto.Charting.Core;

namespace Plotto.Charting.Controls;

public partial class ChromatogramControl
{
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (RequireActivationClick && RenderMode == ChartRenderMode.Renderer && e.ChangedButton == MouseButton.Left)
        {
            if (DeferRendererActivationToParent)
            {
                base.OnMouseDown(e);
                return;
            }

            Focus();
            _activationPressPosition = e.GetPosition(this);
            _awaitingActivationClick = true;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (RenderMode == ChartRenderMode.Editor
            && e.ChangedButton == MouseButton.Left
            && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            Focus();
            var x = ToChartPoint(e.GetPosition(this)).X;
            _peakSplitStaticX.Add(x);
            RequestRender();
            e.Handled = true;
            return;
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_awaitingActivationClick && RequireActivationClick && RenderMode == ChartRenderMode.Renderer)
        {
            var p = e.GetPosition(this);
            if (DistanceSquaredDip(p, _activationPressPosition) > ActivationMoveToleranceDip * ActivationMoveToleranceDip)
            {
                _awaitingActivationClick = false;
                ReleaseMouseCapture();
            }
        }

        if (RenderMode == ChartRenderMode.Editor
            && (Keyboard.Modifiers & ModifierKeys.Control) != 0
            && e.LeftButton == MouseButtonState.Released)
        {
            _peakSplitHoverX = ToChartPoint(e.GetPosition(this)).X;
            InvalidateVisual();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_awaitingActivationClick && RequireActivationClick && RenderMode == ChartRenderMode.Renderer && e.ChangedButton == MouseButton.Left)
        {
            _awaitingActivationClick = false;
            ReleaseMouseCapture();
            var up = e.GetPosition(this);
            if (DistanceSquaredDip(up, _activationPressPosition) <= ActivationMoveToleranceDip * ActivationMoveToleranceDip)
            {
                RenderMode = ChartRenderMode.Editor;
                InvalidateVisual();
                e.Handled = true;
                return;
            }
        }

        base.OnMouseUp(e);
        if (e.ChangedButton == MouseButton.Left)
        {
            _isIntegrationDragActive = false;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && RequireActivationClick && RenderMode == ChartRenderMode.Editor)
        {
            RenderMode = ChartRenderMode.Renderer;
            _awaitingActivationClick = false;
            _peakSplitStaticX.Clear();
            _peakSplitHoverX = null;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);

        if (RenderMode == ChartRenderMode.Editor && e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            if (Mouse.LeftButton == MouseButtonState.Released && IsMouseOver)
            {
                _peakSplitHoverX = ToChartPoint(Mouse.GetPosition(this)).X;
            }

            InvalidateVisual();
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (RenderMode == ChartRenderMode.Editor && e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            _peakSplitHoverX = null;
            InvalidateVisual();
        }
    }

    protected override void OnChartMouseDown(ChartPoint point, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            return;
        }

        _peakSplitStaticX.Clear();
        _peakSplitHoverX = null;
        _isIntegrationDragActive = true;
        IntegrationRegions = Array.Empty<IntegrationRegion>();
        var p = ClampBaselineAnchor(point);
        _activeRegion = new IntegrationRegion(p, p);
        RequestRender();
    }

    protected override void OnChartMouseDrag(ChartPoint point, MouseEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            return;
        }

        if (!_isIntegrationDragActive || _activeRegion is null)
        {
            return;
        }

        var start = _activeRegion.Value.Start;
        _activeRegion = new IntegrationRegion(start, ClampBaselineAnchor(point));
        RequestRender();
    }

    protected override void OnChartMouseUp(ChartPoint point, MouseButtonEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            return;
        }

        if (!_isIntegrationDragActive || _activeRegion is null || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        // Mouse-up only ends the drag. It must not modify line geometry.
        var committed = _activeRegion.Value;
        IntegrationRegions = new List<IntegrationRegion> { committed };
        _activeRegion = null;
        _isIntegrationDragActive = false;
        IntegrationChanged?.Invoke(this, new IntegrationRegionEventArgs(committed));
        RequestRender();
    }

    private static double DistanceSquaredDip(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}
