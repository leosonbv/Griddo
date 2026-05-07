using System;
using System.Windows;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    private static double FloorToRecordStep(double valuePx, double recordHeightPx)
    {
        if (recordHeightPx < 1e-9)
        {
            return valuePx;
        }

        return Math.Floor(valuePx / recordHeightPx + 1e-9) * recordHeightPx;
    }

    /// <summary>
    /// When <see cref="VisibleRecordCount"/> is set, record height tracks viewport height; snap scroll extent so the thumb maps to whole-record offsets.
    /// </summary>
    private double GetAlignedMaxVerticalScrollForSlider(double rawMaxVertical)
    {
        if (_visibleRecordCount <= 0 || Records.Count == 0 || _viewportBodyHeight <= 0)
        {
            return rawMaxVertical;
        }

        var h = GetRecordHeight(0);
        if (h < 1e-6)
        {
            return rawMaxVertical;
        }

        return FloorToRecordStep(rawMaxVertical, h);
    }

    /// <summary>
    /// When <see cref="VisibleRecordCount"/> is set, vertical scroll offset must be a multiple of record height so the top visible scroll record aligns with the body band (resize no longer leaves a partial record at the top).
    /// </summary>
    private double HarmonizeVerticalScrollOffset(double offsetPx)
    {
        if (_isTransposed)
        {
            return Math.Clamp(offsetPx, 0, _verticalScrollBar.Maximum);
        }

        if (_visibleRecordCount <= 0 || Records.Count == 0 || _viewportBodyHeight <= 0)
        {
            return Math.Clamp(offsetPx, 0, _verticalScrollBar.Maximum);
        }

        var h = GetRecordHeight(0);
        if (h < 1e-6)
        {
            return Math.Clamp(offsetPx, 0, _verticalScrollBar.Maximum);
        }

        var rawMax = Math.Max(0, GetScrollableRecordsContentHeight() - GetScrollRecordsViewportHeight());
        var clamped = Math.Clamp(offsetPx, 0, rawMax);
        var maxAligned = FloorToRecordStep(rawMax, h);
        var snapped = FloorToRecordStep(clamped, h);
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
            var fixedRecordsW = GetTransposeFixedRecordsWidth();
            var scrollRecordsViewport = Math.Max(0, _viewportBodyWidth - fixedRecordsW);
            var h = Records.Count > 0 ? GetRecordHeight(0) : GetMinimumRecordThickness() * ContentScale;
            var fr = GetEffectiveFixedRecordCount();
            var scrollRecordsContent = Math.Max(0, Records.Count - fr) * h;
            maxHorizontal = Math.Max(0, scrollRecordsContent - scrollRecordsViewport);

            var fixedColsH = GetFixedFieldsWidth();
            var scrollColsViewport = Math.Max(0, _viewportBodyHeight - fixedColsH);
            var scrollColsContent = 0.0;
            for (var c = _fixedFieldCount; c < Fields.Count; c++)
            {
                scrollColsContent += GetFieldWidth(c);
            }

            maxVertical = Math.Max(0, scrollColsContent - scrollColsViewport);
            horizontalLargeChange = Math.Max(1, scrollRecordsViewport);
            verticalLargeChange = Math.Max(1, scrollColsViewport);
            verticalSmallChange = 16;
        }
        else
        {
            var scrollViewport = GetScrollViewportWidth();
            var scrollContent = GetScrollableContentWidth();
            var scrollRecordsViewport = GetScrollRecordsViewportHeight();
            var scrollRecordsContent = GetScrollableRecordsContentHeight();
            maxHorizontal = Math.Max(0, scrollContent - scrollViewport);
            var rawMaxVertical = Math.Max(0, scrollRecordsContent - scrollRecordsViewport);
            maxVertical = GetAlignedMaxVerticalScrollForSlider(rawMaxVertical);
            horizontalLargeChange = Math.Max(1, _viewportBodyWidth);
            verticalLargeChange = Math.Max(1, scrollRecordsViewport);
            verticalSmallChange = Math.Max(1, GetRecordHeight(0));
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
        SnapVerticalScrollForVisibleRecordFitMode();
    }

    /// <summary>After viewport/record height changes, re-align scroll offset to whole records so the scroll band starts on a record boundary.</summary>
    private void SnapVerticalScrollForVisibleRecordFitMode()
    {
        if (_isTransposed)
        {
            return;
        }

        if (_visibleRecordCount <= 0 || Records.Count == 0 || _viewportBodyHeight <= 0)
        {
            return;
        }

        var h = GetRecordHeight(0);
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
