using System.Windows;
using System.Windows.Input;
using Griddo.Primitives;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    private int HitTestRowHeaderDrag(Point point)
    {
        if (Rows.Count == 0 || _viewportBodyHeight <= 0)
        {
            return -1;
        }

        var minY = ScaledColumnHeaderHeight;
        var maxY = ScaledColumnHeaderHeight + _viewportBodyHeight - 1;
        var clampedY = Math.Clamp(point.Y, minY, maxY);
        var contentY = clampedY - ScaledColumnHeaderHeight + _verticalOffset;
        var row = HitTestRowIndex(contentY);
        return row >= 0 ? row : -1;
    }

    private int HitTestColumnHeaderDrag(Point point)
    {
        if (Columns.Count == 0 || _viewportBodyWidth <= 0)
        {
            return -1;
        }

        var minX = _rowHeaderWidth;
        var maxX = _rowHeaderWidth + _viewportBodyWidth - 1;
        var clampedX = Math.Clamp(point.X, minX, maxX);
        return HitTestColumnHeader(new Point(clampedX, point.Y));
    }

    private GriddoCellAddress HitTestCell(Point point)
    {
        if (point.X < _rowHeaderWidth || point.Y < ScaledColumnHeaderHeight)
        {
            return new GriddoCellAddress(-1, -1);
        }

        if (point.X > _rowHeaderWidth + _viewportBodyWidth || point.Y > ScaledColumnHeaderHeight + _viewportBodyHeight)
        {
            return new GriddoCellAddress(-1, -1);
        }

        var row = HitTestRowIndex(point.Y - ScaledColumnHeaderHeight + _verticalOffset);
        if (row < 0)
        {
            return new GriddoCellAddress(-1, -1);
        }

        if (!TryMapViewportPointToContentX(point.X, out var contentX))
        {
            return new GriddoCellAddress(-1, -1);
        }

        var x = 0.0;
        for (var col = 0; col < Columns.Count; col++)
        {
            var width = GetColumnWidth(col);
            if (contentX >= x && contentX < x + width)
            {
                return new GriddoCellAddress(row, col);
            }

            x += width;
        }

        return new GriddoCellAddress(-1, -1);
    }

    private int HitTestColumnDivider(Point point)
    {
        if (point.Y < 0 || point.Y > ScaledColumnHeaderHeight || point.X < _rowHeaderWidth || point.X > _rowHeaderWidth + _viewportBodyWidth)
        {
            return -1;
        }

        if (!TryMapViewportPointToContentX(point.X, out var contentX))
        {
            return -1;
        }

        var x = 0.0;
        for (var col = 0; col < Columns.Count; col++)
        {
            x += GetColumnWidth(col);
            if (Math.Abs(contentX - x) <= ScaledResizeGrip)
            {
                return col;
            }
        }

        return -1;
    }

    private bool HitTestTopLeftHeaderCell(Point point)
    {
        return point.X >= 0 &&
               point.X <= _rowHeaderWidth &&
               point.Y >= 0 &&
               point.Y <= ScaledColumnHeaderHeight;
    }

    private int HitTestColumnHeader(Point point)
    {
        if (point.Y < 0 || point.Y > ScaledColumnHeaderHeight || point.X < _rowHeaderWidth || point.X > _rowHeaderWidth + _viewportBodyWidth)
        {
            return -1;
        }

        if (!TryMapViewportPointToContentX(point.X, out var contentX))
        {
            return -1;
        }

        var x = 0.0;
        for (var col = 0; col < Columns.Count; col++)
        {
            var width = GetColumnWidth(col);
            if (contentX >= x && contentX < x + width)
            {
                return col;
            }

            x += width;
        }

        return -1;
    }

    private int HitTestRowDivider(Point point)
    {
        if (point.X < 0 || point.X > _rowHeaderWidth || point.Y < ScaledColumnHeaderHeight || point.Y > ScaledColumnHeaderHeight + _viewportBodyHeight)
        {
            return -1;
        }

        var y = 0.0;
        var contentY = point.Y - ScaledColumnHeaderHeight + _verticalOffset;
        for (var row = 0; row < Rows.Count; row++)
        {
            y += GetRowHeight(row);
            if (Math.Abs(contentY - y) <= ScaledResizeGrip)
            {
                return row;
            }
        }

        return -1;
    }

    private int HitTestRowHeader(Point point)
    {
        if (point.X < 0 || point.X > _rowHeaderWidth || point.Y < ScaledColumnHeaderHeight || point.Y > ScaledColumnHeaderHeight + _viewportBodyHeight)
        {
            return -1;
        }

        return HitTestRowIndex(point.Y - ScaledColumnHeaderHeight + _verticalOffset);
    }

    private int HitTestRowIndex(double pointerY)
    {
        if (Rows.Count == 0 || pointerY < 0)
        {
            return -1;
        }

        var rowHeight = GetRowHeight(0);
        var row = (int)(pointerY / rowHeight);
        return row >= 0 && row < Rows.Count ? row : -1;
    }

    private void UpdateResizeCursor(Point point)
    {
        if (HitTestColumnDivider(point) >= 0)
        {
            Cursor = Cursors.SizeWE;
            return;
        }

        if (HitTestRowDivider(point) >= 0)
        {
            Cursor = Cursors.SizeNS;
            return;
        }

        Cursor = Cursors.Arrow;
    }
}
