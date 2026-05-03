using System;
using System.Windows;
using System.Windows.Media;
using Griddo.Columns;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    /// <summary>Leading rows to freeze, limited by row count and how many full rows fit in the body viewport.</summary>
    private int GetEffectiveFixedRowCount()
    {
        if (Rows.Count == 0 || _viewportBodyHeight <= 0)
        {
            return 0;
        }

        var h = GetRowHeight(0);
        if (h <= 1e-6)
        {
            return 0;
        }

        var maxFit = (int)(_viewportBodyHeight / h);
        return Math.Min(Math.Min(_fixedRowCount, Rows.Count), maxFit);
    }

    private double GetFixedRowsHeight() => GetEffectiveFixedRowCount() * GetRowHeight(0);

    private double GetScrollRowsViewportHeight() => Math.Max(0, _viewportBodyHeight - GetFixedRowsHeight());

    private double GetScrollableRowsContentHeight()
    {
        var h = GetRowHeight(0);
        var f = GetEffectiveFixedRowCount();
        return Math.Max(0, Rows.Count - f) * h;
    }

    /// <summary>Top edge of a body row relative to the top of the body strip (below column headers).</summary>
    private double GetRowBodyTopRel(int rowIndex)
    {
        var h = GetRowHeight(0);
        var f = GetEffectiveFixedRowCount();
        if (rowIndex < f)
        {
            return rowIndex * h;
        }

        return f * h + (rowIndex - f) * h - _verticalOffset;
    }

    private void ForEachVisibleRow(Action<int> onRow)
    {
        if (Rows.Count == 0 || _viewportBodyHeight <= 0)
        {
            return;
        }

        var h = GetRowHeight(0);
        var f = GetEffectiveFixedRowCount();
        var bodyH = _viewportBodyHeight;
        for (var r = 0; r < f && r < Rows.Count; r++)
        {
            if (r * h < bodyH)
            {
                onRow(r);
            }
        }

        var scrollTop = f * h;
        var vh = bodyH - scrollTop;
        if (vh <= 0 || f >= Rows.Count)
        {
            return;
        }

        var first = f + (int)Math.Floor(_verticalOffset / h);
        var last = f + (int)Math.Ceiling((_verticalOffset + vh) / h) - 1;
        first = Math.Clamp(first, f, Rows.Count - 1);
        last = Math.Clamp(last, f, Rows.Count - 1);
        for (var r = first; r <= last; r++)
        {
            onRow(r);
        }
    }

    private double GetRowHeight(int rowIndex)
    {
        _ = rowIndex;
        if (_visibleRowCount > 0 && _viewportBodyHeight > 0)
        {
            return _viewportBodyHeight / _visibleRowCount;
        }

        return Math.Max(MinRowHeight, _uniformRowHeight) * ContentScale;
    }

    private void SetUniformRowHeightFromScreen(double screenPixelHeight)
    {
        _uniformRowHeight = Math.Max(MinRowHeight, screenPixelHeight / ContentScale);
        UpdateScrollBars();
    }

    private void SetRowHeightKeepingRowTop(int rowIndex, double newScreenHeight)
    {
        if (Rows.Count == 0)
        {
            SetUniformRowHeightFromScreen(newScreenHeight);
            return;
        }

        var clampedRowIndex = Math.Clamp(rowIndex, 0, Rows.Count - 1);
        var oldHeight = GetRowHeight(clampedRowIndex);
        var oldOffset = _verticalOffset;

        SetUniformRowHeightFromScreen(newScreenHeight);

        var updatedHeight = GetRowHeight(clampedRowIndex);
        var deltaH = updatedHeight - oldHeight;
        var fr = Math.Min(_fixedRowCount, Rows.Count);
        var offsetDelta = fr * deltaH + Math.Max(0, clampedRowIndex - fr) * deltaH;
        SetVerticalOffset(oldOffset + offsetDelta);
    }

    private void AutoSizeRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= Rows.Count)
        {
            return;
        }

        var typeface = new Typeface("Segoe UI");
        var pad = 6 * _contentScale;
        var max = MeasureTextHeight((rowIndex + 1).ToString(), typeface, EffectiveFontSize) + pad;
        foreach (var columnView in Columns)
        {
            var value = columnView.GetValue(Rows[rowIndex]);
            max = Math.Max(max, MeasureCellHeight(value, typeface, EffectiveFontSize) + pad);
        }

        SetRowHeightKeepingRowTop(rowIndex, max);
        InvalidateVisual();
    }

    private Rect GetCellRect(int row, int col)
    {
        if (row < 0 || row >= Rows.Count || col < 0 || col >= Columns.Count)
        {
            return Rect.Empty;
        }

        double x;
        if (col < _fixedColumnCount)
        {
            x = _rowHeaderWidth;
            for (var i = 0; i < col; i++)
            {
                x += GetColumnWidth(i);
            }
        }
        else
        {
            x = _rowHeaderWidth + GetFixedColumnsWidth();
            for (var i = _fixedColumnCount; i < col; i++)
            {
                x += GetColumnWidth(i);
            }

            x -= _horizontalOffset;
        }

        var y = ScaledColumnHeaderHeight + GetRowBodyTopRel(row);
        return new Rect(
            x,
            y,
            GetColumnWidth(col),
            GetRowHeight(row));
    }
}
