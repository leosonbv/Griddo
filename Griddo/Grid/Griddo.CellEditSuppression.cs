using System.Collections.Generic;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    private bool[] _cellEditSuppressedByGridColumn = [];

    /// <summary>
    /// Per visible column: when true, scalar in-place edit, fill-series into cells, clipboard clear/paste, and
    /// checkbox toggles are disabled for that column (hosted plot columns ignore this flag).
    /// </summary>
    public void ReplaceCellEditSuppressionLayout(IReadOnlyList<bool>? flags)
    {
        if (flags is null || flags.Count == 0)
        {
            _cellEditSuppressedByGridColumn = [];
            return;
        }

        var copy = new bool[flags.Count];
        for (var i = 0; i < flags.Count; i++)
        {
            copy[i] = flags[i];
        }

        _cellEditSuppressedByGridColumn = copy;
    }

    /// <summary>Used when building field-chooser rows so persisted lock survives opening the dialog.</summary>
    public bool IsCellEditSuppressedForColumn(int fieldIndex) =>
        (uint)fieldIndex < (uint)_cellEditSuppressedByGridColumn.Length && _cellEditSuppressedByGridColumn[fieldIndex];

    private bool FieldAllowsCellEdit(int fieldIndex) =>
        fieldIndex >= 0
        && fieldIndex < Fields.Count
        && GriddoFieldEditRules.AllowsInPlaceCellEdit(Fields[fieldIndex], fieldIndex, _cellEditSuppressedByGridColumn);
}
