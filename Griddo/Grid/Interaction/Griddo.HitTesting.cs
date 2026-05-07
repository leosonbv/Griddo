using System.Windows;
using System.Windows.Input;
using Griddo.Primitives;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    private int HitTestRecordHeaderDrag(Point point)
    {
        if (Records.Count == 0 || _viewportBodyHeight <= 0)
        {
            return -1;
        }

        if (IsBodyTransposed)
        {
            if (Fields.Count == 0 || _viewportBodyWidth <= 0)
            {
                return -1;
            }

            var minX = _recordHeaderWidth;
            var maxX = _recordHeaderWidth + _viewportBodyWidth - 1;
            var clampedX = Math.Clamp(point.X, minX, maxX);
            var bodyX = clampedX - _recordHeaderWidth;
            var hitRecord = HitTestTransposeRecordFromBodyX(bodyX);
            return hitRecord >= 0 ? hitRecord : -1;
        }

        var minY = ScaledFieldHeaderHeight;
        var maxY = ScaledFieldHeaderHeight + _viewportBodyHeight - 1;
        var clampedY = Math.Clamp(point.Y, minY, maxY);
        var bodyY = clampedY - ScaledFieldHeaderHeight;
        var record = HitTestRecordFromBodyY(bodyY);
        return record >= 0 ? record : -1;
    }

    private int HitTestFieldHeaderDrag(Point point)
    {
        if (Fields.Count == 0 || _viewportBodyWidth <= 0)
        {
            return -1;
        }

        if (IsBodyTransposed)
        {
            var minY = ScaledFieldHeaderHeight;
            var maxY = ScaledFieldHeaderHeight + _viewportBodyHeight - 1;
            var clampedY = Math.Clamp(point.Y, minY, maxY);
            return HitTestFieldHeader(new Point(point.X, clampedY));
        }

        var minX = _recordHeaderWidth;
        var maxX = _recordHeaderWidth + _viewportBodyWidth - 1;
        var clampedX = Math.Clamp(point.X, minX, maxX);
        return HitTestFieldHeader(new Point(clampedX, point.Y));
    }

    private GriddoCellAddress HitTestCell(Point point)
    {
        if (point.X < _recordHeaderWidth || point.Y < ScaledFieldHeaderHeight)
        {
            return new GriddoCellAddress(-1, -1);
        }

        if (point.X > _recordHeaderWidth + _viewportBodyWidth || point.Y > ScaledFieldHeaderHeight + _viewportBodyHeight)
        {
            return new GriddoCellAddress(-1, -1);
        }

        if (IsBodyTransposed)
        {
            var transposeRecord = HitTestTransposeRecordFromBodyX(point.X - _recordHeaderWidth);
            var transposeCol = HitTestTransposeFieldFromBodyY(point.Y - ScaledFieldHeaderHeight);
            if (transposeRecord < 0 || transposeCol < 0)
            {
                return new GriddoCellAddress(-1, -1);
            }

            return new GriddoCellAddress(transposeRecord, transposeCol);
        }

        var record = HitTestRecordFromBodyY(point.Y - ScaledFieldHeaderHeight);
        if (record < 0)
        {
            return new GriddoCellAddress(-1, -1);
        }

        if (!TryMapViewportPointToContentX(point.X, out var contentX))
        {
            return new GriddoCellAddress(-1, -1);
        }

        var x = 0.0;
        for (var col = 0; col < Fields.Count; col++)
        {
            var width = GetFieldWidth(col);
            if (contentX >= x && contentX < x + width)
            {
                return new GriddoCellAddress(record, col);
            }

            x += width;
        }

        return new GriddoCellAddress(-1, -1);
    }

    private int HitTestFieldDivider(Point point)
    {
        if (IsBodyTransposed)
        {
            return HitTestTransposeFieldDividerBetweenBands(point);
        }

        if (point.Y < 0 || point.Y > ScaledFieldHeaderHeight || point.X < _recordHeaderWidth || point.X > _recordHeaderWidth + _viewportBodyWidth)
        {
            return -1;
        }

        if (!TryMapViewportPointToContentX(point.X, out var contentX))
        {
            return -1;
        }

        var x = 0.0;
        for (var col = 0; col < Fields.Count; col++)
        {
            x += GetFieldWidth(col);
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
               point.X <= _recordHeaderWidth &&
               point.Y >= 0 &&
               point.Y <= ScaledFieldHeaderHeight;
    }

    private int HitTestFieldHeader(Point point)
    {
        if (IsBodyTransposed)
        {
            if (point.X < 0 || point.X > _recordHeaderWidth || point.Y < ScaledFieldHeaderHeight || point.Y > ScaledFieldHeaderHeight + _viewportBodyHeight)
            {
                return -1;
            }

            return HitTestTransposeFieldFromBodyY(point.Y - ScaledFieldHeaderHeight);
        }

        if (point.Y < 0 || point.Y > ScaledFieldHeaderHeight || point.X < _recordHeaderWidth || point.X > _recordHeaderWidth + _viewportBodyWidth)
        {
            return -1;
        }

        if (!TryMapViewportPointToContentX(point.X, out var contentX))
        {
            return -1;
        }

        var x = 0.0;
        for (var col = 0; col < Fields.Count; col++)
        {
            var width = GetFieldWidth(col);
            if (contentX >= x && contentX < x + width)
            {
                return col;
            }

            x += width;
        }

        return -1;
    }

    private int HitTestRecordDivider(Point point)
    {
        if (IsBodyTransposed)
        {
            return HitTestTransposeRecordDividerBetweenBands(point);
        }

        if (point.X < 0 || point.X > _recordHeaderWidth || point.Y < ScaledFieldHeaderHeight || point.Y > ScaledFieldHeaderHeight + _viewportBodyHeight)
        {
            return -1;
        }

        var bodyY = point.Y - ScaledFieldHeaderHeight;
        for (var record = 0; record < Records.Count; record++)
        {
            var topRel = GetRecordBodyTopRel(record);
            var h = GetRecordHeight(record);
            var sep = topRel + h;
            if (Math.Abs(bodyY - sep) <= ScaledResizeGrip)
            {
                return record;
            }
        }

        return -1;
    }

    private int HitTestRecordHeader(Point point)
    {
        if (IsBodyTransposed)
        {
            if (point.Y < 0 || point.Y > ScaledFieldHeaderHeight || point.X < _recordHeaderWidth || point.X > _recordHeaderWidth + _viewportBodyWidth)
            {
                return -1;
            }

            return HitTestTransposeRecordFromBodyX(point.X - _recordHeaderWidth);
        }

        if (point.X < 0 || point.X > _recordHeaderWidth || point.Y < ScaledFieldHeaderHeight || point.Y > ScaledFieldHeaderHeight + _viewportBodyHeight)
        {
            return -1;
        }

        return HitTestRecordFromBodyY(point.Y - ScaledFieldHeaderHeight);
    }

    private int HitTestRecordFromBodyY(double bodyY)
    {
        if (Records.Count == 0 || bodyY < 0)
        {
            return -1;
        }

        var h = GetRecordHeight(0);
        var f = GetEffectiveFixedRecordCount();
        var fixedH = f * h;
        if (bodyY < fixedH)
        {
            var r = (int)(bodyY / h);
            return r >= 0 && r < Records.Count ? r : -1;
        }

        var scrollBodyY = bodyY - fixedH;
        var scrollContentY = scrollBodyY + _verticalOffset;
        var r2 = f + (int)(scrollContentY / h);
        return r2 >= 0 && r2 < Records.Count ? r2 : -1;
    }

    private void UpdateResizeCursor(Point point)
    {
        if (IsBodyTransposed)
        {
            if (HitTestFieldDivider(point) >= 0)
            {
                Cursor = Cursors.SizeNS;
                return;
            }

            if (HitTestRecordDivider(point) >= 0)
            {
                Cursor = Cursors.SizeWE;
                return;
            }

            Cursor = Cursors.Arrow;
            return;
        }

        if (HitTestFieldDivider(point) >= 0)
        {
            Cursor = Cursors.SizeWE;
            return;
        }

        if (HitTestRecordDivider(point) >= 0)
        {
            Cursor = Cursors.SizeNS;
            return;
        }

        Cursor = Cursors.Arrow;
    }
}
