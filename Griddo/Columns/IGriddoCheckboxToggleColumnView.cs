namespace Griddo.Columns;

/// <summary>
/// Column whose body cells may render and toggle as a checkbox when <see cref="IsCheckboxCell"/> is true for the row.
/// <see cref="GriddoBoolColumnView"/> implements this with <see cref="IsCheckboxCell"/> always true.
/// </summary>
public interface IGriddoCheckboxToggleColumnView : IGriddoColumnView
{
    bool IsCheckboxCell(object rowSource);
}
