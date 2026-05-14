namespace Griddo.Fields;

/// <summary>
/// Implemented by field views whose column header and in-cell editing reflect bindable, user-writable
/// properties (typically a public instance setter on a reflected member).
/// </summary>
/// <remarks>
/// Product rule: use <see cref="AllowCellEdit"/> (or catalog ctor overrides such as <c>allowCellEdit: false</c> on
/// <c>GriddoFieldView</c>) when a column displays data that must not be edited in the grid even if the backing model
/// exposes a public setter (computed, navigation, or denormalized display). Reflection-based defaults are only a hint.
/// Clipboard paste and clear operate on scalar cells only; they do not drive hosted plot hosts.
/// </remarks>
public interface IGriddoFieldEditableHeaderView
{
    bool UseBoldColumnHeader { get; }

    /// <summary>
    /// When false, the grid does not start in-place text edit, fill-series, clipboard clear/paste into cells, or checkbox toggle for this field.
    /// <see cref="IGriddoHostedFieldView"/> plot/chart columns ignore this flag (they remain interactively editable).
/// Per-layout <c>SuppressCellEdit</c> on persisted field rows (where the host supports it) also blocks these operations
/// and suppresses the bold column-header treatment for that column.
/// </summary>
    bool AllowCellEdit { get; }
}
