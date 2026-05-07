using System.Windows;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    private double EffectiveHorizontalScrollBarThickness => ShowHorizontalScrollBar ? ScrollBarSize : 0;
    private double EffectiveVerticalScrollBarThickness => ShowVerticalScrollBar ? ScrollBarSize : 0;

    private double GetFixedFieldsWidth()
    {
        var n = Math.Clamp(_fixedFieldCount, 0, Fields.Count);
        var w = 0.0;
        for (var i = 0; i < n; i++)
        {
            w += GetFieldWidth(i);
        }

        return w;
    }

    private double GetScrollViewportWidth() => Math.Max(0, _viewportBodyWidth - GetFixedFieldsWidth());

    private double GetScrollableContentWidth()
    {
        var total = 0.0;
        for (var col = _fixedFieldCount; col < Fields.Count; col++)
        {
            total += GetFieldWidth(col);
        }

        return total;
    }

    /// <summary>Maps a point in the field area to horizontal content X (0 = left edge of field 0).</summary>
    private bool TryMapViewportPointToContentX(double pointX, out double contentX)
    {
        if (pointX < _recordHeaderWidth || pointX > _recordHeaderWidth + _viewportBodyWidth)
        {
            contentX = 0;
            return false;
        }

        var rel = pointX - _recordHeaderWidth;
        var fixedW = GetFixedFieldsWidth();
        if (rel < fixedW)
        {
            contentX = rel;
        }
        else
        {
            contentX = fixedW + (rel - fixedW) + _horizontalOffset;
        }

        return true;
    }

    private void GetVisibleScrollFieldRange(out int startCol, out int endCol, out double startX)
    {
        startCol = _fixedFieldCount;
        endCol = _fixedFieldCount - 1;
        startX = _recordHeaderWidth + GetFixedFieldsWidth();

        if (Fields.Count == 0 || _viewportBodyWidth <= 0 || _fixedFieldCount >= Fields.Count)
        {
            return;
        }

        var scrollVp = GetScrollViewportWidth();
        if (scrollVp <= 0)
        {
            return;
        }

        var contentLeft = _horizontalOffset;
        var contentRight = _horizontalOffset + scrollVp;

        var x = 0.0;
        var col = _fixedFieldCount;
        while (col < Fields.Count)
        {
            var width = GetFieldWidth(col);
            if (x + width > contentLeft)
            {
                break;
            }

            x += width;
            col++;
        }

        if (col >= Fields.Count)
        {
            startCol = Fields.Count - 1;
            endCol = Fields.Count - 1;
            startX = _recordHeaderWidth + GetFixedFieldsWidth() + x - _horizontalOffset;
            return;
        }

        startCol = col;
        startX = _recordHeaderWidth + GetFixedFieldsWidth() + x - _horizontalOffset;
        endCol = startCol;
        var cursor = x;
        while (endCol < Fields.Count)
        {
            cursor += GetFieldWidth(endCol);
            if (cursor >= contentRight)
            {
                break;
            }

            endCol++;
        }

        endCol = Math.Clamp(endCol, startCol, Fields.Count - 1);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        const double outerBorderInset = 1;
        _horizontalScrollBar.Measure(availableSize);
        _verticalScrollBar.Measure(availableSize);
        var bodyW = Math.Max(0, availableSize.Width - _recordHeaderWidth - EffectiveVerticalScrollBarThickness - outerBorderInset);
        var bodyH = Math.Max(0, availableSize.Height - ScaledFieldHeaderHeight - EffectiveHorizontalScrollBarThickness - outerBorderInset);
        var bodySize = new Size(bodyW, bodyH);
        _scrollHostCanvas.Measure(bodySize);
        _fixedHostCanvas.Measure(bodySize);
        _scaleFeedbackLayer.Measure(availableSize);
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        const double outerBorderInset = 1;
        var arrangedHorizontalThickness = Math.Max(0, EffectiveHorizontalScrollBarThickness - outerBorderInset);
        var arrangedVerticalThickness = Math.Max(0, EffectiveVerticalScrollBarThickness - outerBorderInset);
        _viewportBodyWidth = Math.Max(0, finalSize.Width - _recordHeaderWidth - EffectiveVerticalScrollBarThickness - outerBorderInset);
        _viewportBodyHeight = Math.Max(0, finalSize.Height - ScaledFieldHeaderHeight - EffectiveHorizontalScrollBarThickness - outerBorderInset);

        _horizontalScrollBar.Arrange(new Rect(
            _recordHeaderWidth,
            Math.Max(0, finalSize.Height - EffectiveHorizontalScrollBarThickness - outerBorderInset),
            _viewportBodyWidth,
            arrangedHorizontalThickness));

        _verticalScrollBar.Arrange(new Rect(
            Math.Max(0, finalSize.Width - EffectiveVerticalScrollBarThickness - outerBorderInset),
            ScaledFieldHeaderHeight,
            arrangedVerticalThickness,
            _viewportBodyHeight));

        UpdateScrollBars();

        UpdateHostCanvasClips();

        var bodyRect = new Rect(_recordHeaderWidth, ScaledFieldHeaderHeight, _viewportBodyWidth, _viewportBodyHeight);
        _scrollHostCanvas.Arrange(bodyRect);
        _fixedHostCanvas.Arrange(bodyRect);

        _scaleFeedbackLayer.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }
}
