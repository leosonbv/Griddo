using System.Collections.Generic;
using Griddo.Fields;

namespace Griddo.Grid;

/// <summary>
/// Central rules for whether a column participates in scalar in-place editing (text edit, fill-series,
/// clear/paste into cells, checkbox toggle). Hosted plot/chart columns are handled separately.
/// </summary>
public static class GriddoFieldEditRules
{
    /// <summary>Chromatogram/spectrum/calibration/stability and other <see cref="IGriddoHostedFieldView"/> columns.</summary>
    public static bool IsHostedPlotColumn(IGriddoFieldView field) => field is IGriddoHostedFieldView;

    /// <summary>
    /// Scalar and checkbox columns: in-place edit is allowed unless suppressed by layout or
    /// <see cref="IGriddoFieldEditableHeaderView.AllowCellEdit"/> is false. Hosted columns are always allowed here
    /// (plot interaction is separate from <see cref="IGriddoCellEditor"/> text edit).
    /// </summary>
    /// <param name="field">Field at <paramref name="fieldIndex"/>.</param>
    /// <param name="fieldIndex">Current column index in <see cref="Griddo.Fields"/>.</param>
    /// <param name="cellEditSuppressedByGridColumn">Parallel to visible columns; true suppresses scalar edit for that column.</param>
    public static bool AllowsInPlaceCellEdit(
        IGriddoFieldView field,
        int fieldIndex,
        IReadOnlyList<bool>? cellEditSuppressedByGridColumn)
    {
        if (field is IGriddoHostedFieldView)
        {
            return true;
        }

        if (cellEditSuppressedByGridColumn is not null
            && fieldIndex >= 0
            && fieldIndex < cellEditSuppressedByGridColumn.Count
            && cellEditSuppressedByGridColumn[fieldIndex])
        {
            return false;
        }

        return field is not IGriddoFieldEditableHeaderView h || h.AllowCellEdit;
    }
}
