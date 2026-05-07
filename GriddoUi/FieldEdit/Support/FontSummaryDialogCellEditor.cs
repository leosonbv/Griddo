using Griddo.Editing;

namespace GriddoUi.FieldEdit;

/// <summary>Dialog "..." button with free text (like <see cref="GriddoTextCellEditor"/>), but empty commit clears instead of sending the launch token.</summary>
public sealed class FontSummaryDialogCellEditor : IGriddoDialogButtonCellEditor
{
    public string ButtonText => "...";
    public string LaunchToken => "...";

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
        if (string.IsNullOrWhiteSpace(editBuffer))
        {
            newValue = string.Empty;
            return true;
        }

        newValue = editBuffer.Trim();
        return true;
    }
}
