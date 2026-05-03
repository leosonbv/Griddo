using System.Windows;
using System.Windows.Media;
using Griddo.Columns;

namespace Griddo.Grid;

public sealed partial class Griddo
{
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
        var offsetDelta = clampedRowIndex * (updatedHeight - oldHeight);
        SetVerticalOffset(oldOffset + offsetDelta);
    }

    private double GetRowTop(int rowIndex)
    {
        return rowIndex * GetRowHeight(0);
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

        var y = GetRowTop(row);
        return new Rect(
            x,
            ScaledColumnHeaderHeight + y - _verticalOffset,
            GetColumnWidth(col),
            GetRowHeight(row));
    }
}
