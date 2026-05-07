using System;
using System.Windows;

namespace Griddo.Grid;

/// <summary>Transposed layout: logical records extend horizontally, logical fields vertically (property-grid style).</summary>
public sealed partial class Griddo
{
    private bool IsBodyTransposed => _isTransposed && Records.Count > 0 && Fields.Count > 0;

    private double GetTransposeFixedRecordsWidth()
    {
        var f = GetEffectiveFixedRecordCount();
        if (f <= 0 || Records.Count == 0)
        {
            return 0;
        }

        return f * GetRecordHeight(0);
    }

    /// <summary>Left edge of record band in body coordinates (uniform record height; <see cref="GetRecordHeight"/> ignores record index).</summary>
    private double GetTransposedRecordBodyLeftRel(int recordIndex)
    {
        var h = GetRecordHeight(0);
        var f = GetEffectiveFixedRecordCount();
        if (recordIndex < f)
        {
            return recordIndex * h;
        }

        return recordIndex * h - _horizontalOffset;
    }

    private double GetTransposedFieldBodyTopRel(int colIndex)
    {
        var f = Math.Clamp(_fixedFieldCount, 0, Fields.Count);
        var top = 0.0;
        for (var c = 0; c < colIndex && c < Fields.Count; c++)
        {
            top += GetFieldWidth(c);
        }

        if (colIndex < f)
        {
            return top;
        }

        var fixedH = 0.0;
        for (var c = 0; c < f; c++)
        {
            fixedH += GetFieldWidth(c);
        }

        var scroll = 0.0;
        for (var c = f; c < colIndex; c++)
        {
            scroll += GetFieldWidth(c);
        }

        return fixedH + scroll - _verticalOffset;
    }

    private void ForEachVisibleFieldForTranspose(Action<int> onCol)
    {
        if (Fields.Count == 0 || _viewportBodyHeight <= 0)
        {
            return;
        }

        var f = Math.Clamp(_fixedFieldCount, 0, Fields.Count);
        var acc = 0.0;
        for (var c = 0; c < f && c < Fields.Count; c++)
        {
            if (acc < _viewportBodyHeight)
            {
                onCol(c);
            }

            acc += GetFieldWidth(c);
        }

        var fixedH = 0.0;
        for (var i = 0; i < f; i++)
        {
            fixedH += GetFieldWidth(i);
        }

        var scrollVp = _viewportBodyHeight - fixedH;
        if (scrollVp <= 0 || f >= Fields.Count)
        {
            return;
        }

        var contentTop = _verticalOffset;
        var contentBottom = _verticalOffset + scrollVp;
        var y = 0.0;
        var col = f;
        while (col < Fields.Count)
        {
            var ch = GetFieldWidth(col);
            if (y + ch > contentTop)
            {
                break;
            }

            y += ch;
            col++;
        }

        if (col >= Fields.Count)
        {
            return;
        }

        var last = col;
        var cursor = y;
        while (last < Fields.Count)
        {
            cursor += GetFieldWidth(last);
            if (cursor >= contentBottom)
            {
                break;
            }

            last++;
        }

        last = Math.Clamp(last, col, Fields.Count - 1);
        for (var c = col; c <= last; c++)
        {
            onCol(c);
        }
    }

    private void ForEachVisibleScrollRecordForTranspose(Action<int> onRecord)
    {
        if (Records.Count == 0 || _viewportBodyWidth <= 0)
        {
            return;
        }

        var h = GetRecordHeight(0);
        var f = GetEffectiveFixedRecordCount();
        for (var r = 0; r < f && r < Records.Count; r++)
        {
            if (r * h < _viewportBodyWidth)
            {
                onRecord(r);
            }
        }

        var fixedW = f * h;
        var scrollVp = _viewportBodyWidth - fixedW;
        if (scrollVp <= 0 || f >= Records.Count)
        {
            return;
        }

        var first = f + (int)Math.Floor(_horizontalOffset / h);
        var last = f + (int)Math.Ceiling((_horizontalOffset + scrollVp) / h) - 1;
        first = Math.Clamp(first, f, Records.Count - 1);
        last = Math.Clamp(last, f, Records.Count - 1);
        for (var r = first; r <= last; r++)
        {
            onRecord(r);
        }
    }

    private int HitTestTransposeRecordFromBodyX(double bodyX)
    {
        if (Records.Count == 0 || bodyX < 0)
        {
            return -1;
        }

        var h = GetRecordHeight(0);
        var f = GetEffectiveFixedRecordCount();
        var fixedW = f * h;
        if (bodyX < fixedW)
        {
            var r = (int)(bodyX / h);
            return r >= 0 && r < Records.Count ? r : -1;
        }

        var scrollX = bodyX - fixedW + _horizontalOffset;
        var r2 = f + (int)(scrollX / h);
        return r2 >= 0 && r2 < Records.Count ? r2 : -1;
    }

    private int HitTestTransposeFieldFromBodyY(double bodyY)
    {
        if (Fields.Count == 0 || bodyY < 0)
        {
            return -1;
        }

        var f = Math.Clamp(_fixedFieldCount, 0, Fields.Count);
        var y = 0.0;
        for (var c = 0; c < f; c++)
        {
            var ch = GetFieldWidth(c);
            if (bodyY >= y && bodyY < y + ch)
            {
                return c;
            }

            y += ch;
        }

        if (f >= Fields.Count)
        {
            return -1;
        }

        var scrollY = bodyY - y + _verticalOffset;
        var acc = 0.0;
        for (var c = f; c < Fields.Count; c++)
        {
            var ch = GetFieldWidth(c);
            if (scrollY >= acc && scrollY < acc + ch)
            {
                return c;
            }

            acc += ch;
        }

        return -1;
    }

    private int HitTestTransposeFieldDividerBetweenBands(Point point)
    {
        // In transposed mode, field-resize is only allowed from the left field-header strip.
        if (point.X < 0
            || point.X > _recordHeaderWidth
            || point.Y < ScaledFieldHeaderHeight
            || point.Y > ScaledFieldHeaderHeight + _viewportBodyHeight)
        {
            return -1;
        }

        var bodyY = point.Y - ScaledFieldHeaderHeight;
        var best = -1;
        var bestDist = double.PositiveInfinity;
        for (var c = 0; c < Fields.Count; c++)
        {
            var boundary = GetTransposedFieldBodyTopRel(c) + GetFieldWidth(c);
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

    private int HitTestTransposeRecordDividerBetweenBands(Point point)
    {
        // In transposed mode, record-resize is only allowed from the top record-header strip.
        if (point.Y < 0
            || point.Y > ScaledFieldHeaderHeight
            || point.X < _recordHeaderWidth
            || point.X > _recordHeaderWidth + _viewportBodyWidth)
        {
            return -1;
        }

        var bodyX = point.X - _recordHeaderWidth;
        var h = GetRecordHeight(0);
        var best = -1;
        var bestDist = double.PositiveInfinity;
        for (var r = 0; r < Records.Count; r++)
        {
            var sep = GetTransposedRecordBodyLeftRel(r) + h;
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
