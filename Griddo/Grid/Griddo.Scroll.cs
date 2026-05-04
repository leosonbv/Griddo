using System;
using System.Windows;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    private static double FloorToRowStep(double valuePx, double rowHeightPx)
    {
        if (rowHeightPx < 1e-9)
        {
            return valuePx;
        }

        return Math.Floor(valuePx / rowHeightPx + 1e-9) * rowHeightPx;
    }

    /// <summary>
    /// When <see cref="VisibleRowCount"/> is set, row height tracks viewport height; snap scroll extent so the thumb maps to whole-row offsets.
    /// </summary>
    private double GetAlignedMaxVerticalScrollForSlider(double rawMaxVertical)
    {
        if (_visibleRowCount <= 0 || Rows.Count == 0 || _viewportBodyHeight <= 0)
        {
            return rawMaxVertical;
        }

        var h = GetRowHeight(0);
        if (h < 1e-6)
        {
            return rawMaxVertical;
        }

        return FloorToRowStep(rawMaxVertical, h);
    }

    /// <summary>
    /// When <see cref="VisibleRowCount"/> is set, vertical scroll offset must be a multiple of row height so the top visible scroll row aligns with the body band (resize no longer leaves a partial row at the top).
    /// </summary>
    private double HarmonizeVerticalScrollOffset(double offsetPx)
    {
        if (_isTransposed)
        {
            return Math.Clamp(offsetPx, 0, _verticalScrollBar.Maximum);
        }

        if (_visibleRowCount <= 0 || Rows.Count == 0 || _viewportBodyHeight <= 0)
        {
            return Math.Clamp(offsetPx, 0, _verticalScrollBar.Maximum);
        }

        var h = GetRowHeight(0);
        if (h < 1e-6)
        {
            return Math.Clamp(offsetPx, 0, _verticalScrollBar.Maximum);
        }

        var rawMax = Math.Max(0, GetScrollableRowsContentHeight() - GetScrollRowsViewportHeight());
        var clamped = Math.Clamp(offsetPx, 0, rawMax);
        var maxAligned = FloorToRowStep(rawMax, h);
        var snapped = FloorToRowStep(clamped, h);
        return Math.Clamp(snapped, 0, maxAligned);
    }

    private void UpdateScrollBars()
    {
        double maxHorizontal;
        double maxVertical;
        double horizontalLargeChange;
        double verticalLargeChange;
        double verticalSmallChange;

        if (IsBodyTransposed)
        {
            var fixedRowsW = GetTransposeFixedRowsWidth();
            var scrollRowsViewport = Math.Max(0, _viewportBodyWidth - fixedRowsW);
            var h = Rows.Count > 0 ? GetRowHeight(0) : MinRowHeight * ContentScale;
            var fr = GetEffectiveFixedRowCount();
            var scrollRowsContent = Math.Max(0, Rows.Count - fr) * h;
            maxHorizontal = Math.Max(0, scrollRowsContent - scrollRowsViewport);

            var fixedColsH = GetFixedColumnsWidth();
            var scrollColsViewport = Math.Max(0, _viewportBodyHeight - fixedColsH);
            var scrollColsContent = 0.0;
            for (var c = _fixedColumnCount; c < Columns.Count; c++)
            {
                scrollColsContent += GetColumnWidth(c);
            }

            maxVertical = Math.Max(0, scrollColsContent - scrollColsViewport);
            horizontalLargeChange = Math.Max(1, scrollRowsViewport);
            verticalLargeChange = Math.Max(1, scrollColsViewport);
            verticalSmallChange = 16;
        }
        else
        {
            var scrollViewport = GetScrollViewportWidth();
            var scrollContent = GetScrollableContentWidth();
            var scrollRowsViewport = GetScrollRowsViewportHeight();
            var scrollRowsContent = GetScrollableRowsContentHeight();
            maxHorizontal = Math.Max(0, scrollContent - scrollViewport);
            var rawMaxVertical = Math.Max(0, scrollRowsContent - scrollRowsViewport);
            maxVertical = GetAlignedMaxVerticalScrollForSlider(rawMaxVertical);
            horizontalLargeChange = Math.Max(1, _viewportBodyWidth);
            verticalLargeChange = Math.Max(1, scrollRowsViewport);
            verticalSmallChange = Math.Max(1, GetRowHeight(0));
        }

        _horizontalScrollBar.LargeChange = horizontalLargeChange;
        _horizontalScrollBar.SmallChange = 16;
        _horizontalScrollBar.Maximum = maxHorizontal;
        _horizontalScrollBar.Visibility = ShowHorizontalScrollBar ? Visibility.Visible : Visibility.Collapsed;

        _verticalScrollBar.LargeChange = verticalLargeChange;
        _verticalScrollBar.SmallChange = verticalSmallChange;
        _verticalScrollBar.Maximum = maxVertical;
        _verticalScrollBar.Visibility = ShowVerticalScrollBar ? Visibility.Visible : Visibility.Collapsed;

        SetHorizontalOffset(_horizontalOffset);
        SetVerticalOffset(_verticalOffset);
        SnapVerticalScrollForVisibleRowFitMode();
    }

    /// <summary>After viewport/row height changes, re-align scroll offset to whole rows so the scroll band starts on a row boundary.</summary>
    private void SnapVerticalScrollForVisibleRowFitMode()
    {
        if (_isTransposed)
        {
            return;
        }

        if (_visibleRowCount <= 0 || Rows.Count == 0 || _viewportBodyHeight <= 0)
        {
            return;
        }

        var h = GetRowHeight(0);
        if (h < 1e-6)
        {
            return;
        }

        var v = HarmonizeVerticalScrollOffset(_verticalOffset);
        if (Math.Abs(v - _verticalOffset) < double.Epsilon)
        {
            return;
        }

        _verticalOffset = v;
        if (Math.Abs(_verticalScrollBar.Value - v) > double.Epsilon)
        {
            _verticalScrollBar.Value = v;
        }

        InvalidateVisual();
    }

    private void SetHorizontalOffset(double value)
    {
        var clamped = Math.Clamp(value, 0, _horizontalScrollBar.Maximum);
        if (Math.Abs(clamped - _horizontalOffset) < double.Epsilon)
        {
            return;
        }

        _horizontalOffset = clamped;
        if (Math.Abs(_horizontalScrollBar.Value - clamped) > double.Epsilon)
        {
            _horizontalScrollBar.Value = clamped;
        }

        InvalidateVisual();
    }

    private void SetVerticalOffset(double value)
    {
        var clamped = Math.Clamp(value, 0, _verticalScrollBar.Maximum);
        if (Math.Abs(clamped - _verticalOffset) < double.Epsilon)
        {
            return;
        }

        _verticalOffset = clamped;
        if (Math.Abs(_verticalScrollBar.Value - clamped) > double.Epsilon)
        {
            _verticalScrollBar.Value = clamped;
        }

        InvalidateVisual();
    }

    private void OnHorizontalScrollChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _horizontalOffset = e.NewValue;
        InvalidateVisual();
    }

    private void OnVerticalScrollChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _verticalOffset = e.NewValue;
        InvalidateVisual();
    }
}
