namespace Griddo.Fields;

/// <summary>
/// Implemented by field views whose column header and in-cell editing reflect bindable, user-writable
/// properties (typically a public instance setter on a reflected member).
/// </summary>
public interface IGriddoFieldEditableHeaderView
{
    bool UseBoldColumnHeader { get; }

    /// <summary>
    /// When false, the grid does not start in-place text edit, fill-series, clipboard clear/paste into cells, or checkbox toggle for this field.
    /// <see cref="IGriddoHostedFieldView"/> plot/chart columns ignore this flag (they remain interactively editable).
    /// </summary>
    bool AllowCellEdit { get; }
}
