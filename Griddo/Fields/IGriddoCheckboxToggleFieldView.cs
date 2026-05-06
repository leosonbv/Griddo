namespace Griddo.Fields;

/// <summary>
/// Field whose body cells may render and toggle as a checkbox when <see cref="IsCheckboxCell"/> is true for the record.
/// <see cref="GriddoBoolFieldView"/> implements this with <see cref="IsCheckboxCell"/> always true.
/// </summary>
public interface IGriddoCheckboxToggleFieldView : IGriddoFieldView
{
    bool IsCheckboxCell(object recordSource);
}
