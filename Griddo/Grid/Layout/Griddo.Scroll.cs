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

    /// <summary>Largest cumulative field width at or below <paramref name="offsetPx"/> (scrollable columns only).</summary>
    private double FloorToCumulativeFieldScrollOffset(double offsetPx, int firstFieldIndex)
    {
        var cumulative = 0.0;
        var first = Math.Clamp(firstFieldIndex, 0, Fields.Count);
        for (var c = first; c < Fields.Count; c++)
        {
            var w = GetFieldWidth(c);
            if (cumulative + w > offsetPx + 1e-9)
            {
                break;
            }

            cumulative += w;
        }

        return cumulative;
    }

    /// <summary>Smallest scroll offset &gt; <paramref name="currentOffsetPx"/> aligned to a scroll-column start (non-transposed).</summary>
    private double GetNextHorizontalScrollColumnStart(double currentOffsetPx, double rawMaxHorizontal)
    {
        var cum = 0.0;
        for (var col = _fixedFieldCount; col < Fields.Count; col++)
        {
            if (cum > currentOffsetPx + 1e-9)
            {
                return Math.Min(rawMaxHorizontal, cum);
            }

            cum += GetFieldWidth(col);
        }

        return rawMaxHorizontal;
    }

    /// <summary>Largest scroll offset &lt; <paramref name="currentOffsetPx"/> aligned to a scroll-column start (non-transposed).</summary>
    private double GetPreviousHorizontalScrollColumnStart(double currentOffsetPx)
    {
        var cum = 0.0;
        var prevStart = 0.0;
        for (var col = _fixedFieldCount; col < Fields.Count; col++)
        {
            if (cum >= currentOffsetPx - 1e-9)
            {
                return prevStart;
            }

            prevStart = cum;
            cum += GetFieldWidth(col);
        }

        return prevStart;
    }

    private static double GetNextTransposedHorizontalScrollRecordStep(double currentOffsetPx, double rawMax, double recordHeightPx)
    {
        if (recordHeightPx < 1e-9)
        {
            return Math.Min(rawMax, currentOffsetPx);
        }

        var step = Math.Ceiling((currentOffsetPx + 1e-9) / recordHeightPx) * recordHeightPx;
        return Math.Min(rawMax, step);
    }

    private static double GetPreviousTransposedHorizontalScrollRecordStep(double currentOffsetPx, double recordHeightPx)
    {
        if (recordHeightPx < 1e-9)
        {
            return 0;
        }

        return Math.Max(0, Math.Floor((currentOffsetPx - 1e-9) / recordHeightPx) * recordHeightPx);
    }

    private double GetTransposedRawMaxHorizontalScroll()
    {
        var fixedRecordsW = GetTransposeFixedRecordsWidth();
        var scrollRecordsViewport = Math.Max(0, _viewportBodyWidth - fixedRecordsW);
        var h = Records.Count > 0 ? GetRecordHeight(0) : 0;
        var fr = GetEffectiveFixedRecordCount();
        var scrollRecordsContent = Math.Max(0, Records.Count - fr) * h;
        return Math.Max(0, scrollRecordsContent - scrollRecordsViewport);
    }

    private double GetTransposedRawMaxVerticalScroll()
    {
        var fixedColsH = GetFixedFieldsWidth();
        var scrollColsViewport = Math.Max(0, _viewportBodyHeight - fixedColsH);
        var scrollColsContent = 0.0;
        for (var c = _fixedFieldCount; c < Fields.Count; c++)
        {
            scrollColsContent += GetFieldWidth(c);
        }

        return Math.Max(0, scrollColsContent - scrollColsViewport);
    }

    /// <summary>Snap scroll extent to whole-record offsets so the thumb maps to aligned rows.</summary>
    private double GetAlignedMaxVerticalScrollForSlider(double rawMaxVertical)
    {
        if (_isTransposed)
        {
            if (Fields.Count == 0 || _viewportBodyHeight <= 0)
            {
                return rawMaxVertical;
            }

            return ShouldSnapVerticalScrollToFieldBorder(_verticalOffset)
                ? Math.Max(0, rawMaxVertical)
                : rawMaxVertical;
        }

        if (Records.Count == 0 || _viewportBodyHeight <= 0)
        {
            return rawMaxVertical;
        }

        var h = GetRecordHeight(0);
        if (h < 1e-6)
        {
            return rawMaxVertical;
        }

        return ShouldSnapVerticalScrollToRecordBorder(_verticalOffset)
            ? Math.Max(0, rawMaxVertical)
            : rawMaxVertical;
    }

    private double GetAlignedMaxHorizontalScrollForSlider(double rawMaxHorizontal)
    {
        if (_isTransposed)
        {
            if (Records.Count == 0 || _viewportBodyWidth <= 0)
            {
                return rawMaxHorizontal;
            }

            var h = GetRecordHeight(0);
            if (h < 1e-6)
            {
                return rawMaxHorizontal;
            }

            return ShouldSnapHorizontalScrollToRecordBorder(_horizontalOffset)
                ? Math.Max(0, rawMaxHorizontal)
                : rawMaxHorizontal;
        }

        if (Fields.Count == 0 || _viewportBodyWidth <= 0)
        {
            return rawMaxHorizontal;
        }

        return ShouldSnapHorizontalScrollToFieldBorder(_horizontalOffset)
            ? Math.Max(0, rawMaxHorizontal)
            : rawMaxHorizontal;
    }

    private static bool ShouldSnapToTrailingEdge(double offsetPx, double rawMax, double stepPx)
    {
        if (rawMax <= 1e-9 || stepPx <= 1e-9)
        {
            return false;
        }

        return offsetPx >= rawMax - stepPx * 0.5 + 1e-9;
    }

    /// <summary>
    /// True when the scrollable body shows exactly one scrollable row and that row is taller than the scroll viewport.
    /// </summary>
    private bool ShouldSnapVerticalScrollToRecordBorder(double offsetPx)
    {
        if (Records.Count == 0 || _viewportBodyHeight <= 0)
        {
            return true;
        }

        var h = GetRecordHeight(0);
        if (h < 1e-6)
        {
            return true;
        }

        var fixedRecordCount = GetEffectiveFixedRecordCount();
        var scrollViewportHeight = GetScrollRecordsViewportHeight();
        if (scrollViewportHeight <= 0 || fixedRecordCount >= Records.Count)
        {
            return true;
        }

        var viewportTop = offsetPx;
        var viewportBottom = offsetPx + scrollViewportHeight;
        var visibleScrollRecords = 0;
        for (var record = fixedRecordCount; record < Records.Count; record++)
        {
            var top = (record - fixedRecordCount) * h;
            var bottom = top + h;
            if (bottom <= viewportTop + 1e-9 || top >= viewportBottom - 1e-9)
            {
                continue;
            }

            visibleScrollRecords++;
            if (visibleScrollRecords > 1)
            {
                return true;
            }
        }

        if (visibleScrollRecords != 1)
        {
            return true;
        }

        return h <= scrollViewportHeight + 1e-9;
    }

    /// <summary>
    /// True when the scrollable body shows exactly one scrollable column and that column is wider than the scroll viewport.
    /// </summary>
    private bool ShouldSnapHorizontalScrollToFieldBorder(double offsetPx)
    {
        if (Fields.Count <= _fixedFieldCount || _viewportBodyWidth <= 0)
        {
            return true;
        }

        var scrollViewportWidth = GetScrollViewportWidth();
        if (scrollViewportWidth <= 0)
        {
            return true;
        }

        var viewportLeft = offsetPx;
        var viewportRight = offsetPx + scrollViewportWidth;
        var visibleScrollFields = 0;
        double soleFieldWidth = 0;
        var contentLeft = 0.0;
        for (var col = _fixedFieldCount; col < Fields.Count; col++)
        {
            var width = GetFieldWidth(col);
            var right = contentLeft + width;
            if (right > viewportLeft + 1e-9 && contentLeft < viewportRight - 1e-9)
            {
                visibleScrollFields++;
                soleFieldWidth = width;
                if (visibleScrollFields > 1)
                {
                    return true;
                }
            }

            contentLeft = right;
        }

        if (visibleScrollFields != 1)
        {
            return true;
        }

        return soleFieldWidth <= scrollViewportWidth + 1e-9;
    }

    /// <summary>
    /// Transposed layout: horizontal scrolling walks records instead of fields.
    /// </summary>
    private bool ShouldSnapHorizontalScrollToRecordBorder(double offsetPx)
    {
        if (Records.Count == 0 || _viewportBodyWidth <= 0)
        {
            return true;
        }

        var h = GetRecordHeight(0);
        if (h < 1e-6)
        {
            return true;
        }

        var fixedRecordsWidth = GetTransposeFixedRecordsWidth();
        var scrollViewportWidth = Math.Max(0, _viewportBodyWidth - fixedRecordsWidth);
        if (scrollViewportWidth <= 0)
        {
            return true;
        }

        var fixedRecordCount = GetEffectiveFixedRecordCount();
        if (fixedRecordCount >= Records.Count)
        {
            return true;
        }

        var viewportLeft = offsetPx;
        var viewportRight = offsetPx + scrollViewportWidth;
        var visibleScrollRecords = 0;
        for (var record = fixedRecordCount; record < Records.Count; record++)
        {
            var left = (record - fixedRecordCount) * h;
            var right = left + h;
            if (right <= viewportLeft + 1e-9 || left >= viewportRight - 1e-9)
            {
                continue;
            }

            visibleScrollRecords++;
            if (visibleScrollRecords > 1)
            {
                return true;
            }
        }

        if (visibleScrollRecords != 1)
        {
            return true;
        }

        return h <= scrollViewportWidth + 1e-9;
    }

    /// <summary>
    /// Transposed layout: vertical scrolling walks fields instead of records.
    /// </summary>
    private bool ShouldSnapVerticalScrollToFieldBorder(double offsetPx)
    {
        if (Fields.Count <= _fixedFieldCount || _viewportBodyHeight <= 0)
        {
            return true;
        }

        var fixedFieldsHeight = GetFixedFieldsWidth();
        var scrollViewportHeight = Math.Max(0, _viewportBodyHeight - fixedFieldsHeight);
        if (scrollViewportHeight <= 0)
        {
            return true;
        }

        var viewportTop = offsetPx;
        var viewportBottom = offsetPx + scrollViewportHeight;
        var visibleScrollFields = 0;
        double soleFieldHeight = 0;
        var contentTop = 0.0;
        for (var col = _fixedFieldCount; col < Fields.Count; col++)
        {
            var height = GetFieldWidth(col);
            var bottom = contentTop + height;
            if (bottom > viewportTop + 1e-9 && contentTop < viewportBottom - 1e-9)
            {
                visibleScrollFields++;
                soleFieldHeight = height;
                if (visibleScrollFields > 1)
                {
                    return true;
                }
            }

            contentTop = bottom;
        }

        if (visibleScrollFields != 1)
        {
            return true;
        }

        return soleFieldHeight <= scrollViewportHeight + 1e-9;
    }

    /// <summary>
    /// Keep vertical scroll offset aligned to full record-height steps so the top visible row border
    /// always lands on the top edge of the scroll viewport.
    /// </summary>
    private double HarmonizeVerticalScrollOffset(double offsetPx)
    {
        if (_isTransposed)
        {
            if (Fields.Count == 0 || _viewportBodyHeight <= 0)
            {
                return Math.Clamp(offsetPx, 0, _verticalScrollBar.Maximum);
            }

            var rawMax = GetTransposedRawMaxVerticalScroll();
            var clamped = Math.Clamp(offsetPx, 0, rawMax);
            if (!ShouldSnapVerticalScrollToFieldBorder(offsetPx))
            {
                return clamped;
            }

            var tailStep = Fields.Count > _fixedFieldCount
                ? GetFieldWidth(Fields.Count - 1)
                : Math.Max(1, _viewportBodyHeight - GetFixedFieldsWidth());
            var snapped = ShouldSnapToTrailingEdge(clamped, rawMax, tailStep)
                ? rawMax
                : FloorToCumulativeFieldScrollOffset(clamped, _fixedFieldCount);
            return Math.Clamp(snapped, 0, rawMax);
        }

        if (Records.Count == 0 || _viewportBodyHeight <= 0)
        {
            return Math.Clamp(offsetPx, 0, _verticalScrollBar.Maximum);
        }

        var h = GetRecordHeight(0);
        if (h < 1e-6)
        {
            return Math.Clamp(offsetPx, 0, _verticalScrollBar.Maximum);
        }

        var rawMaxVertical = Math.Max(0, GetScrollableRecordsContentHeight() - GetScrollRecordsViewportHeight());
        var clampedVertical = Math.Clamp(offsetPx, 0, rawMaxVertical);
        if (!ShouldSnapVerticalScrollToRecordBorder(offsetPx))
        {
            return clampedVertical;
        }

        var snappedVertical = ShouldSnapToTrailingEdge(clampedVertical, rawMaxVertical, h)
            ? rawMaxVertical
            : FloorToRecordStep(clampedVertical, h);
        return Math.Clamp(snappedVertical, 0, rawMaxVertical);
    }

    /// <summary>
    /// Keep horizontal scroll offset aligned to whole field-width steps so the left visible column border
    /// always lands on the left edge of the scroll viewport.
    /// </summary>
    private double HarmonizeHorizontalScrollOffset(double offsetPx)
    {
        if (_isTransposed)
        {
            if (Records.Count == 0 || _viewportBodyWidth <= 0)
            {
                return Math.Clamp(offsetPx, 0, _horizontalScrollBar.Maximum);
            }

            var h = GetRecordHeight(0);
            if (h < 1e-6)
            {
                return Math.Clamp(offsetPx, 0, _horizontalScrollBar.Maximum);
            }

            var rawMax = GetTransposedRawMaxHorizontalScroll();
            var clamped = Math.Clamp(offsetPx, 0, rawMax);
            if (!ShouldSnapHorizontalScrollToRecordBorder(offsetPx))
            {
                return clamped;
            }

            var snapped = ShouldSnapToTrailingEdge(clamped, rawMax, h)
                ? rawMax
                : FloorToRecordStep(clamped, h);
            return Math.Clamp(snapped, 0, rawMax);
        }

        if (Fields.Count == 0 || _viewportBodyWidth <= 0)
        {
            return Math.Clamp(offsetPx, 0, _horizontalScrollBar.Maximum);
        }

        var rawMaxHorizontal = Math.Max(0, GetScrollableContentWidth() - GetScrollViewportWidth());
        var clampedHorizontal = Math.Clamp(offsetPx, 0, rawMaxHorizontal);
        if (!ShouldSnapHorizontalScrollToFieldBorder(offsetPx))
        {
            return clampedHorizontal;
        }

        var tailStep = Fields.Count > _fixedFieldCount
            ? GetFieldWidth(Fields.Count - 1)
            : GetScrollViewportWidth();
        var snappedHorizontal = ShouldSnapToTrailingEdge(clampedHorizontal, rawMaxHorizontal, tailStep)
            ? rawMaxHorizontal
            : FloorToCumulativeFieldScrollOffset(clampedHorizontal, _fixedFieldCount);
        return Math.Clamp(snappedHorizontal, 0, rawMaxHorizontal);
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
            var rawMaxHorizontal = Math.Max(0, scrollRecordsContent - scrollRecordsViewport);
            maxHorizontal = GetAlignedMaxHorizontalScrollForSlider(rawMaxHorizontal);

            var fixedColsH = GetFixedFieldsWidth();
            var scrollColsViewport = Math.Max(0, _viewportBodyHeight - fixedColsH);
            var rawMaxVertical = GetTransposedRawMaxVerticalScroll();
            maxVertical = GetAlignedMaxVerticalScrollForSlider(rawMaxVertical);
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
            var rawMaxHorizontal = Math.Max(0, scrollContent - scrollViewport);
            maxHorizontal = GetAlignedMaxHorizontalScrollForSlider(rawMaxHorizontal);
            var rawMaxVertical = Math.Max(0, scrollRecordsContent - scrollRecordsViewport);
            maxVertical = GetAlignedMaxVerticalScrollForSlider(rawMaxVertical);
            horizontalLargeChange = Math.Max(1, _viewportBodyWidth);
            verticalLargeChange = Math.Max(1, scrollRecordsViewport);
            verticalSmallChange = Math.Max(1, GetRecordHeight(0));
        }

        _horizontalScrollBar.LargeChange = horizontalLargeChange;
        _horizontalScrollBar.SmallChange = 16;
        _horizontalScrollBar.Maximum = maxHorizontal;
        _horizontalScrollBar.ViewportSize = Math.Max(1, horizontalLargeChange);
        _horizontalScrollBar.Visibility = ShowHorizontalScrollBar ? Visibility.Visible : Visibility.Collapsed;

        _verticalScrollBar.LargeChange = verticalLargeChange;
        _verticalScrollBar.SmallChange = verticalSmallChange;
        _verticalScrollBar.Maximum = maxVertical;
        _verticalScrollBar.ViewportSize = Math.Max(1, verticalLargeChange);
        _verticalScrollBar.Visibility = ShowVerticalScrollBar ? Visibility.Visible : Visibility.Collapsed;

        SetHorizontalOffset(_horizontalOffset);
        SetVerticalOffset(_verticalOffset);
        SnapScrollOffsetsToRuler();
    }

    /// <summary>After viewport or ruler size changes, re-align scroll offsets to row/column boundaries.</summary>
    private void SnapScrollOffsetsToRuler()
    {
        var nextHorizontal = HarmonizeHorizontalScrollOffset(_horizontalOffset);
        var nextVertical = HarmonizeVerticalScrollOffset(_verticalOffset);
        var changed = false;

        if (Math.Abs(nextHorizontal - _horizontalOffset) > double.Epsilon)
        {
            _horizontalOffset = nextHorizontal;
            if (Math.Abs(_horizontalScrollBar.Value - nextHorizontal) > double.Epsilon)
            {
                _horizontalScrollBar.Value = nextHorizontal;
            }

            changed = true;
        }

        if (Math.Abs(nextVertical - _verticalOffset) > double.Epsilon)
        {
            _verticalOffset = nextVertical;
            if (Math.Abs(_verticalScrollBar.Value - nextVertical) > double.Epsilon)
            {
                _verticalScrollBar.Value = nextVertical;
            }

            changed = true;
        }

        if (changed)
        {
            InvalidateVisual();
        }
    }

    private void SetHorizontalOffset(double value)
    {
        var clamped = HarmonizeHorizontalScrollOffset(value);
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
        var clamped = HarmonizeVerticalScrollOffset(value);
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
        var target = e.NewValue;
        var delta = e.NewValue - e.OldValue;
        var sc = _horizontalScrollBar.SmallChange;
        if (sc > 1e-9 && Math.Abs(Math.Abs(delta) - sc) < 1e-6)
        {
            if (_isTransposed)
            {
                if (Records.Count > 0 && _viewportBodyWidth > 1e-9)
                {
                    var h = GetRecordHeight(0);
                    var rawMax = GetTransposedRawMaxHorizontalScroll();
                    var hOld = HarmonizeHorizontalScrollOffset(e.OldValue);
                    if (delta > 0)
                    {
                        target = GetNextTransposedHorizontalScrollRecordStep(hOld, rawMax, h);
                    }
                    else if (delta < 0)
                    {
                        target = GetPreviousTransposedHorizontalScrollRecordStep(hOld, h);
                    }
                }
            }
            else if (Fields.Count > _fixedFieldCount && _viewportBodyWidth > 1e-9)
            {
                var rawMax = Math.Max(0, GetScrollableContentWidth() - GetScrollViewportWidth());
                var hOld = HarmonizeHorizontalScrollOffset(e.OldValue);
                if (delta > 0)
                {
                    target = GetNextHorizontalScrollColumnStart(hOld, rawMax);
                }
                else if (delta < 0)
                {
                    target = GetPreviousHorizontalScrollColumnStart(hOld);
                }
            }
        }

        var harmonized = HarmonizeHorizontalScrollOffset(target);
        _horizontalOffset = harmonized;
        if (Math.Abs(e.NewValue - harmonized) > double.Epsilon && Math.Abs(_horizontalScrollBar.Value - harmonized) > double.Epsilon)
        {
            _horizontalScrollBar.Value = harmonized;
        }

        InvalidateVisual();
    }

    private void OnVerticalScrollChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var harmonized = HarmonizeVerticalScrollOffset(e.NewValue);
        _verticalOffset = harmonized;
        if (Math.Abs(e.NewValue - harmonized) > double.Epsilon && Math.Abs(_verticalScrollBar.Value - harmonized) > double.Epsilon)
        {
            _verticalScrollBar.Value = harmonized;
        }

        InvalidateVisual();
    }
}
