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
        var clamped = Math.Max(MinRowHeight, screenPixelHeight / ContentScale);
        if (Math.Abs(_uniformRowHeight - clamped) < double.Epsilon)
        {
            return;
        }

        _uniformRowHeight = clamped;
        UniformRowHeightChanged?.Invoke(this, EventArgs.Empty);
        UpdateScrollBars();
    }

    /// <summary>
    /// Fill-rows mode derives height from the viewport and ignores <see cref="UniformRowHeight"/>.
    /// Before row-divider hit math (anchor Y), switch to uniform height equal to what is currently drawn
    /// so the first mouse-move does not jump row layout under the cursor.
    /// </summary>
    private void ExitFillRowsUsingCurrentDisplayedRowHeight()
    {
        if (_visibleRowCount <= 0)
        {
            return;
        }

        var hScreen = GetRowHeight(0);
        VisibleRowCount = 0;
        SetUniformRowHeightFromScreen(hScreen);
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
        var transposeResize = _isTransposed && Rows.Count > 0 && Columns.Count > 0;
        double oldScrollExtentMax;
        double oldScrollOffset;
        if (transposeResize)
        {
            var fixedRowsW0 = GetTransposeFixedRowsWidth();
            var scrollVp0 = Math.Max(0, _viewportBodyWidth - fixedRowsW0);
            var h0 = GetRowHeight(0);
            var fr0 = GetEffectiveFixedRowCount();
            var scrollContent0 = Math.Max(0, Rows.Count - fr0) * h0;
            oldScrollExtentMax = Math.Max(0, scrollContent0 - scrollVp0);
            oldScrollOffset = _horizontalOffset;
        }
        else
        {
            oldScrollExtentMax = Math.Max(0, GetScrollableRowsContentHeight() - GetScrollRowsViewportHeight());
            oldScrollOffset = _verticalOffset;
        }

        // Fill-rows mode (VisibleRowCount > 0) forces row height from viewport / count and ignores
        // UniformRowHeight. Manual divider drag must leave that mode so the dragged height applies.
        if (_visibleRowCount > 0)
        {
            VisibleRowCount = 0;
        }

        SetUniformRowHeightFromScreen(newScreenHeight);

        if (transposeResize)
        {
            var fixedRowsW = GetTransposeFixedRowsWidth();
            var scrollRowsViewport = Math.Max(0, _viewportBodyWidth - fixedRowsW);
            var hAfter = GetRowHeight(0);
            var frT = GetEffectiveFixedRowCount();
            var scrollRowsContent = Math.Max(0, Rows.Count - frT) * hAfter;
            var newMaxHorizontal = Math.Max(0, scrollRowsContent - scrollRowsViewport);
            if (oldScrollExtentMax <= 1e-6 && newMaxHorizontal <= 1e-6)
            {
                return;
            }

            if (_isResizingRow)
            {
                return;
            }

            var updatedHeightT = GetRowHeight(clampedRowIndex);
            var deltaHT = updatedHeightT - oldHeight;
            var frRows = Math.Min(_fixedRowCount, Rows.Count);
            var offsetDeltaT = frRows * deltaHT + Math.Max(0, clampedRowIndex - frRows) * deltaHT;
            SetHorizontalOffset(oldScrollOffset + offsetDeltaT);
            return;
        }

        var newMaxVerticalOffset = Math.Max(0, GetScrollableRowsContentHeight() - GetScrollRowsViewportHeight());
        if (oldScrollExtentMax <= 1e-6 && newMaxVerticalOffset <= 1e-6)
        {
            // No vertical scroll range before/after resize: keep natural layout flow.
            // Applying top-preservation offset compensation in this mode causes
            // cursor/divider drift while dragging.
            return;
        }

        // During interactive row resize, scroll compensation fights closed-form height math and
        // causes divider Y to oscillate until the body fills the viewport.
        if (_isResizingRow)
        {
            return;
        }

        var updatedHeight = GetRowHeight(clampedRowIndex);
        var deltaH = updatedHeight - oldHeight;
        var fr = Math.Min(_fixedRowCount, Rows.Count);
        var offsetDelta = fr * deltaH + Math.Max(0, clampedRowIndex - fr) * deltaH;
        SetVerticalOffset(oldScrollOffset + offsetDelta);
    }

    /// <summary>
    /// Uniform row height h so the bottom edge of row <paramref name="dividerRowIndex"/> lies at
    /// <paramref name="bodyPointerY"/> (Y relative to top of body strip, below column headers).
    /// Uses a frozen effective fixed-row count during divider drag to avoid f(h) feedback oscillation.
    /// </summary>
    private double GetUniformRowHeightScreenFromDividerBodyY(int dividerRowIndex, double bodyPointerY)
    {
        if (Rows.Count == 0 || _viewportBodyHeight <= 0)
        {
            return Math.Max(MinRowHeight * ContentScale, bodyPointerY);
        }

        var k = Math.Clamp(dividerRowIndex, 0, Rows.Count - 1);
        // Must match live layout (frozen + scroll); a frozen snapshot can disagree with
        // GetRowBodyTopRel when effective fixed-row count changes with row height.
        var f = GetEffectiveFixedRowCount();
        var hScreen = k < f
            ? bodyPointerY / (k + 1)
            : (bodyPointerY + _verticalOffset) / (k + 1);
        return Math.Max(MinRowHeight * ContentScale, hScreen);
    }

    /// <summary>
    /// Uniform row height h so the right edge of row <paramref name="dividerRowIndex"/> lies at
    /// <paramref name="bodyPointerX"/> (X relative to the left edge of the body strip, after row headers).
    /// Mirror of <see cref="GetUniformRowHeightScreenFromDividerBodyY"/> for transposed layout (rows scroll horizontally).
    /// </summary>
    private double GetUniformRowHeightScreenFromDividerBodyX(int dividerRowIndex, double bodyPointerX)
    {
        if (Rows.Count == 0 || _viewportBodyWidth <= 0)
        {
            return Math.Max(MinRowHeight * ContentScale, bodyPointerX);
        }

        var k = Math.Clamp(dividerRowIndex, 0, Rows.Count - 1);
        var f = GetEffectiveFixedRowCount();
        var hScreen = k < f
            ? bodyPointerX / (k + 1)
            : (bodyPointerX + _horizontalOffset) / (k + 1);
        return Math.Max(MinRowHeight * ContentScale, hScreen);
    }

    /// <summary>One-shot scroll adjustment after interactive row-height drag (see <see cref="SetRowHeightKeepingRowTop"/>).</summary>
    private void ApplyInteractiveRowResizeScrollPreservation(int dividerRowIndex, double rowHeightAtDragStart, double scrollOffsetAtDragStart)
    {
        if (Rows.Count == 0 || dividerRowIndex < 0)
        {
            return;
        }

        if (IsBodyTransposed)
        {
            var fixedRowsW = GetTransposeFixedRowsWidth();
            var scrollRowsViewport = Math.Max(0, _viewportBodyWidth - fixedRowsW);
            var h = GetRowHeight(0);
            var frT = GetEffectiveFixedRowCount();
            var scrollRowsContent = Math.Max(0, Rows.Count - frT) * h;
            var maxH = Math.Max(0, scrollRowsContent - scrollRowsViewport);
            if (maxH <= 1e-6)
            {
                return;
            }

            var clampedT = Math.Clamp(dividerRowIndex, 0, Rows.Count - 1);
            var newHt = GetRowHeight(clampedT);
            var deltaHt = newHt - rowHeightAtDragStart;
            if (Math.Abs(deltaHt) < 1e-9)
            {
                return;
            }

            var frRows = Math.Min(_fixedRowCount, Rows.Count);
            var offsetDeltaT = frRows * deltaHt + Math.Max(0, clampedT - frRows) * deltaHt;
            SetHorizontalOffset(scrollOffsetAtDragStart + offsetDeltaT);
            return;
        }

        var maxV = Math.Max(0, GetScrollableRowsContentHeight() - GetScrollRowsViewportHeight());
        if (maxV <= 1e-6)
        {
            return;
        }

        var clamped = Math.Clamp(dividerRowIndex, 0, Rows.Count - 1);
        var newH = GetRowHeight(clamped);
        var deltaH = newH - rowHeightAtDragStart;
        if (Math.Abs(deltaH) < 1e-9)
        {
            return;
        }

        var fr = Math.Min(_fixedRowCount, Rows.Count);
        var offsetDelta = fr * deltaH + Math.Max(0, clamped - fr) * deltaH;
        SetVerticalOffset(scrollOffsetAtDragStart + offsetDelta);
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

        if (IsBodyTransposed)
        {
            var tx = _rowHeaderWidth + GetTransposedRowBodyLeftRel(row);
            var ty = ScaledColumnHeaderHeight + GetTransposedColumnBodyTopRel(col);
            return new Rect(tx, ty, GetRowHeight(row), GetColumnWidth(col));
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
