using System.Globalization;

namespace Griddo.Editing;

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

/// <summary>Inline text edit for boolean values (F2 / typing); commit accepts common true/false literals.</summary>
public sealed class GriddoBoolCellEditor : IGriddoCellEditor
{
    public bool CanStartWith(char inputChar) =>
        inputChar is 'y' or 'Y' or 'n' or 'N' or 't' or 'T' or 'f' or 'F' or '0' or '1';

    public string BeginEdit(object? currentValue, char? firstCharacter = null)
    {
        if (firstCharacter.HasValue && CanStartWith(firstCharacter.Value))
        {
            return firstCharacter.Value.ToString();
        }

        return currentValue switch
        {
            bool b => b ? bool.TrueString : bool.FalseString,
            null => string.Empty,
            _ => currentValue.ToString() ?? string.Empty
        };
    }

    public bool TryCommit(string editBuffer, out object? newValue)
    {
        if (string.IsNullOrWhiteSpace(editBuffer))
        {
            newValue = false;
            return true;
        }

        var t = editBuffer.Trim();
        if (bool.TryParse(t, out var parsed))
        {
            newValue = parsed;
            return true;
        }

        switch (t.ToUpperInvariant())
        {
            case "Y":
            case "YES":
            case "T":
            case "TRUE":
            case "1":
            case "ON":
                newValue = true;
                return true;
            case "N":
            case "NO":
            case "F":
            case "FALSE":
            case "0":
            case "OFF":
                newValue = false;
                return true;
            default:
                newValue = null;
                return false;
        }
    }
}

public static class GriddoCellEditors
{
    public static readonly IGriddoCellEditor Text = new GriddoTextCellEditor();
    public static readonly IGriddoCellEditor Number = new GriddoNumberCellEditor();
    public static readonly IGriddoCellEditor Bool = new GriddoBoolCellEditor();
}
