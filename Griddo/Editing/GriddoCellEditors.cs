using System.Globalization;

namespace Griddo;

public interface IGriddoCellEditor
{
    bool CanStartWith(char inputChar);
    string BeginEdit(object? currentValue, char? firstCharacter = null);
    bool TryCommit(string editBuffer, out object? newValue);
}

public sealed class GriddoTextCellEditor : IGriddoCellEditor
{
    public bool CanStartWith(char inputChar) => !char.IsControl(inputChar);

    public string BeginEdit(object? currentValue, char? firstCharacter = null)
    {
        if (firstCharacter.HasValue && !char.IsControl(firstCharacter.Value))
        {
            return firstCharacter.Value.ToString();
        }

        return currentValue?.ToString() ?? string.Empty;
    }

    public bool TryCommit(string editBuffer, out object? newValue)
    {
        newValue = editBuffer;
        return true;
    }
}

public sealed class GriddoNumberCellEditor : IGriddoCellEditor
{
    public bool CanStartWith(char inputChar)
        => char.IsDigit(inputChar) || inputChar is '-' or '+' or '.';

    public string BeginEdit(object? currentValue, char? firstCharacter = null)
    {
        if (firstCharacter.HasValue && !char.IsControl(firstCharacter.Value))
        {
            return firstCharacter.Value.ToString();
        }

        return currentValue?.ToString() ?? string.Empty;
    }

    public bool TryCommit(string editBuffer, out object? newValue)
    {
        if (double.TryParse(editBuffer, NumberStyles.Any, CultureInfo.CurrentCulture, out var numeric))
        {
            newValue = numeric;
            return true;
        }

        newValue = null;
        return false;
    }
}

public static class GriddoCellEditors
{
    public static readonly IGriddoCellEditor Text = new GriddoTextCellEditor();
    public static readonly IGriddoCellEditor Number = new GriddoNumberCellEditor();
}
