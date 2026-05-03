using System.Windows;
using System.Windows.Input;
using Plotto.Charting.Core;

namespace Plotto.Charting.Controls;

public partial class ChromatogramControl
{
    // -------------------------------------------------------------------------
    // Mouse — routed overrides (activation, peak split, then base chart)
    // -------------------------------------------------------------------------

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (TryHandleRendererActivationMouseDown(e))
        {
            return;
        }

        if (TryHandlePeakSplitAnchorClick(e))
        {
            return;
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        CancelActivationIfMovedBeyondSlop(e);

        if (ShouldUpdatePeakSplitHover(e))
        {
            _peakSplitHoverX = ToChartPoint(e.GetPosition(this)).X;
            InvalidateVisual();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (TryCompleteRendererActivationClick(e))
        {
            return;
        }

        base.OnMouseUp(e);

        if (e.ChangedButton == MouseButton.Left)
        {
            _isIntegrationDragActive = false;
        }
    }

    // -------------------------------------------------------------------------
    // Keyboard — escape deactivates editor; Ctrl toggles peak-split hover
    // -------------------------------------------------------------------------

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (TryHandleEscapeDeactivateEditor(e))
        {
            return;
        }

        base.OnKeyDown(e);

        if (RenderMode == ChartRenderMode.Editor && e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            RefreshPeakSplitHoverFromPointer();
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

    // -------------------------------------------------------------------------
    // Chart gestures — manual integration (when Ctrl is not held for peak split)
    // -------------------------------------------------------------------------

    protected override void OnChartMouseDown(ChartPoint point, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsPeakSplitModifierHeld)
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
        if (IsPeakSplitModifierHeld || !_isIntegrationDragActive || _activeRegion is null)
        {
            return;
        }

        var start = _activeRegion.Value.Start;
        _activeRegion = new IntegrationRegion(start, ClampBaselineAnchor(point));
        RequestRender();
    }

    protected override void OnChartMouseUp(ChartPoint point, MouseButtonEventArgs e)
    {
        if (IsPeakSplitModifierHeld || !_isIntegrationDragActive || _activeRegion is null || e.ChangedButton != MouseButton.Left)
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

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static bool IsPeakSplitModifierHeld => (Keyboard.Modifiers & ModifierKeys.Control) != 0;

    private bool IsAwaitingRendererActivation =>
        _awaitingActivationClick && RequireActivationClick && RenderMode == ChartRenderMode.Renderer;

    private bool TryHandleRendererActivationMouseDown(MouseButtonEventArgs e)
    {
        if (!RequireActivationClick || RenderMode != ChartRenderMode.Renderer || e.ChangedButton != MouseButton.Left)
        {
            return false;
        }

        if (DeferRendererActivationToParent)
        {
            base.OnMouseDown(e);
            return true;
        }

        Focus();
        _activationPressPosition = e.GetPosition(this);
        _awaitingActivationClick = true;
        CaptureMouse();
        e.Handled = true;
        return true;
    }

    private bool TryHandlePeakSplitAnchorClick(MouseButtonEventArgs e)
    {
        if (RenderMode != ChartRenderMode.Editor || e.ChangedButton != MouseButton.Left || !IsPeakSplitModifierHeld)
        {
            return false;
        }

        Focus();
        _peakSplitStaticX.Add(ToChartPoint(e.GetPosition(this)).X);
        RequestRender();
        e.Handled = true;
        return true;
    }

    private void CancelActivationIfMovedBeyondSlop(MouseEventArgs e)
    {
        if (!IsAwaitingRendererActivation)
        {
            return;
        }

        var p = e.GetPosition(this);
        if (LogicalDistanceSquaredDip(p, _activationPressPosition) > ActivationMoveToleranceDip * ActivationMoveToleranceDip)
        {
            _awaitingActivationClick = false;
            ReleaseMouseCapture();
        }
    }

    private bool ShouldUpdatePeakSplitHover(MouseEventArgs e) =>
        RenderMode == ChartRenderMode.Editor
        && IsPeakSplitModifierHeld
        && e.LeftButton == MouseButtonState.Released;

    private bool TryCompleteRendererActivationClick(MouseButtonEventArgs e)
    {
        if (!IsAwaitingRendererActivation || e.ChangedButton != MouseButton.Left)
        {
            return false;
        }

        _awaitingActivationClick = false;
        ReleaseMouseCapture();
        var up = e.GetPosition(this);
        if (LogicalDistanceSquaredDip(up, _activationPressPosition) > ActivationMoveToleranceDip * ActivationMoveToleranceDip)
        {
            return false;
        }

        RenderMode = ChartRenderMode.Editor;
        InvalidateVisual();
        e.Handled = true;
        return true;
    }

    private bool TryHandleEscapeDeactivateEditor(KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !RequireActivationClick || RenderMode != ChartRenderMode.Editor)
        {
            return false;
        }

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
        return true;
    }

    private void RefreshPeakSplitHoverFromPointer()
    {
        if (Mouse.LeftButton == MouseButtonState.Released && IsMouseOver)
        {
            _peakSplitHoverX = ToChartPoint(Mouse.GetPosition(this)).X;
        }
    }

    private static double LogicalDistanceSquaredDip(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}
