using System;
using System.Windows;

namespace Griddo.Grid;

/// <summary>Transposed layout: logical rows extend horizontally, logical columns vertically (property-grid style).</summary>
public sealed partial class Griddo
{
    private bool IsBodyTransposed => _isTransposed && Rows.Count > 0 && Columns.Count > 0;

    private double GetTransposeFixedRowsWidth()
    {
        var f = GetEffectiveFixedRowCount();
        if (f <= 0 || Rows.Count == 0)
        {
            return 0;
        }

        return f * GetRowHeight(0);
    }

    /// <summary>Left edge of row band in body coordinates (uniform row height; <see cref="GetRowHeight"/> ignores row index).</summary>
    private double GetTransposedRowBodyLeftRel(int rowIndex)
    {
        var h = GetRowHeight(0);
        var f = GetEffectiveFixedRowCount();
        if (rowIndex < f)
        {
            return rowIndex * h;
        }

        return rowIndex * h - _horizontalOffset;
    }

    private double GetTransposedColumnBodyTopRel(int colIndex)
    {
        var f = Math.Clamp(_fixedColumnCount, 0, Columns.Count);
        var top = 0.0;
        for (var c = 0; c < colIndex && c < Columns.Count; c++)
        {
            top += GetColumnWidth(c);
        }

        if (colIndex < f)
        {
            return top;
        }

        var fixedH = 0.0;
        for (var c = 0; c < f; c++)
        {
            fixedH += GetColumnWidth(c);
        }

        var scroll = 0.0;
        for (var c = f; c < colIndex; c++)
        {
            scroll += GetColumnWidth(c);
        }

        return fixedH + scroll - _verticalOffset;
    }

    private void ForEachVisibleColumnForTranspose(Action<int> onCol)
    {
        if (Columns.Count == 0 || _viewportBodyHeight <= 0)
        {
            return;
        }

        var f = Math.Clamp(_fixedColumnCount, 0, Columns.Count);
        var acc = 0.0;
        for (var c = 0; c < f && c < Columns.Count; c++)
        {
            if (acc < _viewportBodyHeight)
            {
                onCol(c);
            }

            acc += GetColumnWidth(c);
        }

        var fixedH = 0.0;
        for (var i = 0; i < f; i++)
        {
            fixedH += GetColumnWidth(i);
        }

        var scrollVp = _viewportBodyHeight - fixedH;
        if (scrollVp <= 0 || f >= Columns.Count)
        {
            return;
        }

        var contentTop = _verticalOffset;
        var contentBottom = _verticalOffset + scrollVp;
        var y = 0.0;
        var col = f;
        while (col < Columns.Count)
        {
            var ch = GetColumnWidth(col);
            if (y + ch > contentTop)
            {
                break;
            }

            y += ch;
            col++;
        }

        if (col >= Columns.Count)
        {
            return;
        }

        var last = col;
        var cursor = y;
        while (last < Columns.Count)
        {
            cursor += GetColumnWidth(last);
            if (cursor >= contentBottom)
            {
                break;
            }

            last++;
        }

        last = Math.Clamp(last, col, Columns.Count - 1);
        for (var c = col; c <= last; c++)
        {
            onCol(c);
        }
    }

    private void ForEachVisibleScrollRowForTranspose(Action<int> onRow)
    {
        if (Rows.Count == 0 || _viewportBodyWidth <= 0)
        {
            return;
        }

        var h = GetRowHeight(0);
        var f = GetEffectiveFixedRowCount();
        for (var r = 0; r < f && r < Rows.Count; r++)
        {
            if (r * h < _viewportBodyWidth)
            {
                onRow(r);
            }
        }

        var fixedW = f * h;
        var scrollVp = _viewportBodyWidth - fixedW;
        if (scrollVp <= 0 || f >= Rows.Count)
        {
            return;
        }

        var first = f + (int)Math.Floor(_horizontalOffset / h);
        var last = f + (int)Math.Ceiling((_horizontalOffset + scrollVp) / h) - 1;
        first = Math.Clamp(first, f, Rows.Count - 1);
        last = Math.Clamp(last, f, Rows.Count - 1);
        for (var r = first; r <= last; r++)
        {
            onRow(r);
        }
    }

    private int HitTestTransposeRowFromBodyX(double bodyX)
    {
        if (Rows.Count == 0 || bodyX < 0)
        {
            return -1;
        }

        var h = GetRowHeight(0);
        var f = GetEffectiveFixedRowCount();
        var fixedW = f * h;
        if (bodyX < fixedW)
        {
            var r = (int)(bodyX / h);
            return r >= 0 && r < Rows.Count ? r : -1;
        }

        var scrollX = bodyX - fixedW + _horizontalOffset;
        var r2 = f + (int)(scrollX / h);
        return r2 >= 0 && r2 < Rows.Count ? r2 : -1;
    }

    private int HitTestTransposeColumnFromBodyY(double bodyY)
    {
        if (Columns.Count == 0 || bodyY < 0)
        {
            return -1;
        }

        var f = Math.Clamp(_fixedColumnCount, 0, Columns.Count);
        var y = 0.0;
        for (var c = 0; c < f; c++)
        {
            var ch = GetColumnWidth(c);
            if (bodyY >= y && bodyY < y + ch)
            {
                return c;
            }

            y += ch;
        }

        if (f >= Columns.Count)
        {
            return -1;
        }

        var scrollY = bodyY - y + _verticalOffset;
        var acc = 0.0;
        for (var c = f; c < Columns.Count; c++)
        {
            var ch = GetColumnWidth(c);
            if (scrollY >= acc && scrollY < acc + ch)
            {
                return c;
            }

            acc += ch;
        }

        return -1;
    }

    private int HitTestTransposeColumnDividerBetweenBands(Point point)
    {
        // Horizontal band boundaries: include the left column-header strip (same Y as body).
        if (point.X < 0
            || point.X > _rowHeaderWidth + _viewportBodyWidth
            || point.Y < ScaledColumnHeaderHeight
            || point.Y > ScaledColumnHeaderHeight + _viewportBodyHeight)
        {
            return -1;
        }

        var bodyY = point.Y - ScaledColumnHeaderHeight;
        var best = -1;
        var bestDist = double.PositiveInfinity;
        for (var c = 0; c < Columns.Count; c++)
        {
            var boundary = GetTransposedColumnBodyTopRel(c) + GetColumnWidth(c);
            var d = Math.Abs(bodyY - boundary);
            if (d > ScaledResizeGrip)
            {
                continue;
            }

            if (d < bestDist - 1e-6)
            {
                bestDist = d;
                best = c;
            }
        }

        return best;
    }

    private int HitTestTransposeRowDividerBetweenBands(Point point)
    {
        // Vertical boundaries align with the top row-header strip (same X as body), not only the cell area.
        if (point.Y < 0
            || point.Y > ScaledColumnHeaderHeight + _viewportBodyHeight
            || point.X < _rowHeaderWidth
            || point.X > _rowHeaderWidth + _viewportBodyWidth)
        {
            return -1;
        }

        var bodyX = point.X - _rowHeaderWidth;
        var h = GetRowHeight(0);
        var best = -1;
        var bestDist = double.PositiveInfinity;
        for (var r = 0; r < Rows.Count; r++)
        {
            var sep = GetTransposedRowBodyLeftRel(r) + h;
            var d = Math.Abs(bodyX - sep);
            if (d > ScaledResizeGrip)
            {
                continue;
            }

            if (d < bestDist - 1e-6)
            {
                bestDist = d;
                best = r;
            }
        }

        return best;
    }
}
