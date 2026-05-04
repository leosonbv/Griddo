using Griddo.Columns;
using Griddo.Primitives;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    private bool IsCheckboxToggleCell(GriddoCellAddress cell)
    {
        if (!cell.IsValid
            || cell.RowIndex < 0
            || cell.RowIndex >= Rows.Count
            || cell.ColumnIndex < 0
            || cell.ColumnIndex >= Columns.Count)
        {
            return false;
        }

        var row = Rows[cell.RowIndex];
        return Columns[cell.ColumnIndex] is IGriddoCheckboxToggleColumnView toggle && toggle.IsCheckboxCell(row);
    }

    private void ToggleBoolCell(GriddoCellAddress cell)
    {
        if (cell.RowIndex < 0 || cell.RowIndex >= Rows.Count || cell.ColumnIndex < 0 || cell.ColumnIndex >= Columns.Count)
        {
            return;
        }

        if (Columns[cell.ColumnIndex] is not IGriddoCheckboxToggleColumnView column)
        {
            return;
        }

        var row = Rows[cell.RowIndex];
        if (!column.IsCheckboxCell(row))
        {
            return;
        }

        var cur = column.GetValue(row);
        var next = cur is not true;
        if (column.TrySetValue(row, next))
        {
            InvalidateVisual();
        }
    }
}
