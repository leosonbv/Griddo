using System.Linq;

namespace Griddo.Editing;

/// <summary>
/// Grid configurator "Font" column: dropdown for default / font dialog, accepts pasted summary lines.
/// </summary>
public sealed class GriddoConfiguratorFontSummaryCellEditor : IGriddoOptionsCellEditor
{
    public const string DefaultToken = "(default)";
    public const string ChooseFontToken = "Choose font…";

    private static readonly string[] OptionsList = [DefaultToken, ChooseFontToken];

    public IReadOnlyList<string> Options => OptionsList;
    public bool AllowMultiple => false;

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
        var t = (editBuffer ?? string.Empty).Trim();
        var line = t.Replace("\r\n", "\n").Split('\n').FirstOrDefault(static x => x.Trim().Length > 0)?.Trim() ?? string.Empty;
        if (line.Length == 0)
        {
            newValue = DefaultToken;
            return true;
        }

        if (string.Equals(line, DefaultToken, StringComparison.OrdinalIgnoreCase))
        {
            newValue = DefaultToken;
            return true;
        }

        if (string.Equals(line, ChooseFontToken, StringComparison.OrdinalIgnoreCase)
            || string.Equals(line, "...", StringComparison.Ordinal))
        {
            newValue = ChooseFontToken;
            return true;
        }

        newValue = line;
        return true;
    }

    public IReadOnlyList<string> ParseValues(string editBuffer)
    {
        var value = (editBuffer ?? string.Empty).Trim();
        return value.Length == 0 ? [] : [value];
    }

    public string FormatValues(IEnumerable<string> values)
    {
        var first = values.FirstOrDefault(static x => !string.IsNullOrWhiteSpace(x))?.Trim();
        return first ?? string.Empty;
    }
}
