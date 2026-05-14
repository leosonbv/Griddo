using Griddo.Fields;
using Griddo.Primitives;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    private bool IsCheckboxToggleCell(GriddoCellAddress cell)
    {
        if (!cell.IsValid
            || cell.RecordIndex < 0
            || cell.RecordIndex >= Records.Count
            || cell.FieldIndex < 0
            || cell.FieldIndex >= Fields.Count)
        {
            return false;
        }

        var record = Records[cell.RecordIndex];
        return Fields[cell.FieldIndex] is IGriddoCheckboxToggleFieldView toggle && toggle.IsCheckboxCell(record);
    }

    private void ToggleBoolCell(GriddoCellAddress cell)
    {
        if (cell.RecordIndex < 0 || cell.RecordIndex >= Records.Count || cell.FieldIndex < 0 || cell.FieldIndex >= Fields.Count)
        {
            return;
        }

        if (Fields[cell.FieldIndex] is not IGriddoCheckboxToggleFieldView field)
        {
            return;
        }

        if (!FieldAllowsCellEdit(cell.FieldIndex))
        {
            return;
        }

        var record = Records[cell.RecordIndex];
        if (!field.IsCheckboxCell(record))
        {
            return;
        }

        var cur = field.GetValue(record);
        var next = cur is not true;
        if (field.TrySetValue(record, next))
        {
            InvalidateVisual();
        }
    }
}
