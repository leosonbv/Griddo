using System.Windows;
using System.Windows.Media;

namespace Griddo;

public sealed partial class Griddo
{
    private double GetFixedColumnsWidth()
    {
        var n = Math.Clamp(_fixedColumnCount, 0, Columns.Count);
        var w = 0.0;
        for (var i = 0; i < n; i++)
        {
            w += GetColumnWidth(i);
        }

        return w;
    }

    private double GetScrollViewportWidth() => Math.Max(0, _viewportBodyWidth - GetFixedColumnsWidth());

    private double GetScrollableContentWidth()
    {
        var total = 0.0;
        for (var col = _fixedColumnCount; col < Columns.Count; col++)
        {
            total += GetColumnWidth(col);
        }

        return total;
    }

    /// <summary>Maps a point in the column area to horizontal content X (0 = left edge of column 0).</summary>
    private bool TryMapViewportPointToContentX(double pointX, out double contentX)
    {
        if (pointX < _rowHeaderWidth || pointX > _rowHeaderWidth + _viewportBodyWidth)
        {
            contentX = 0;
            return false;
        }

        var rel = pointX - _rowHeaderWidth;
        var fixedW = GetFixedColumnsWidth();
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

    private void GetVisibleScrollColumnRange(out int startCol, out int endCol, out double startX)
    {
        startCol = _fixedColumnCount;
        endCol = _fixedColumnCount - 1;
        startX = _rowHeaderWidth + GetFixedColumnsWidth();

        if (Columns.Count == 0 || _viewportBodyWidth <= 0 || _fixedColumnCount >= Columns.Count)
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
        var col = _fixedColumnCount;
        while (col < Columns.Count)
        {
            var width = GetColumnWidth(col);
            if (x + width > contentLeft)
            {
                break;
            }

            x += width;
            col++;
        }

        if (col >= Columns.Count)
        {
            startCol = Columns.Count - 1;
            endCol = Columns.Count - 1;
            startX = _rowHeaderWidth + GetFixedColumnsWidth() + x - _horizontalOffset;
            return;
        }

        startCol = col;
        startX = _rowHeaderWidth + GetFixedColumnsWidth() + x - _horizontalOffset;
        endCol = startCol;
        var cursor = x;
        while (endCol < Columns.Count)
        {
            cursor += GetColumnWidth(endCol);
            if (cursor >= contentRight)
            {
                break;
            }

            endCol++;
        }

        endCol = Math.Clamp(endCol, startCol, Columns.Count - 1);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        const double outerBorderInset = 1;
        _horizontalScrollBar.Measure(availableSize);
        _verticalScrollBar.Measure(availableSize);
        var bodyW = Math.Max(0, availableSize.Width - _rowHeaderWidth - ScrollBarSize - outerBorderInset);
        var bodyH = Math.Max(0, availableSize.Height - ScaledColumnHeaderHeight - ScrollBarSize - outerBorderInset);
        var bodySize = new Size(bodyW, bodyH);
        _scrollHostCanvas.Measure(bodySize);
        _fixedHostCanvas.Measure(bodySize);
        _scaleFeedbackLayer.Measure(availableSize);
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        const double outerBorderInset = 1;
        var arrangedScrollBarThickness = Math.Max(0, ScrollBarSize - outerBorderInset);
        _viewportBodyWidth = Math.Max(0, finalSize.Width - _rowHeaderWidth - ScrollBarSize - outerBorderInset);
        _viewportBodyHeight = Math.Max(0, finalSize.Height - ScaledColumnHeaderHeight - ScrollBarSize - outerBorderInset);

        _horizontalScrollBar.Arrange(new Rect(
            _rowHeaderWidth,
            Math.Max(0, finalSize.Height - ScrollBarSize - outerBorderInset),
            _viewportBodyWidth,
            arrangedScrollBarThickness));

        _verticalScrollBar.Arrange(new Rect(
            Math.Max(0, finalSize.Width - ScrollBarSize - outerBorderInset),
            ScaledColumnHeaderHeight,
            arrangedScrollBarThickness,
            _viewportBodyHeight));

        UpdateScrollBars();

        UpdateHostCanvasClips();

        var bodyRect = new Rect(_rowHeaderWidth, ScaledColumnHeaderHeight, _viewportBodyWidth, _viewportBodyHeight);
        _scrollHostCanvas.Arrange(bodyRect);
        _fixedHostCanvas.Arrange(bodyRect);

        _scaleFeedbackLayer.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }
}
