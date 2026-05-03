using Griddo.Columns;
using Griddo.Primitives;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    private void ToggleBoolCell(GriddoCellAddress cell)
    {
        if (cell.RowIndex < 0 || cell.RowIndex >= Rows.Count || cell.ColumnIndex < 0 || cell.ColumnIndex >= Columns.Count)
        {
            return;
        }

        if (Columns[cell.ColumnIndex] is not GriddoBoolColumnView column)
        {
            return;
        }

        var row = Rows[cell.RowIndex];
        var cur = column.GetValue(row);
        var next = cur is not true;
        if (column.TrySetValue(row, next))
        {
            InvalidateVisual();
        }
    }
}
