using System.Globalization;
using System.Text.RegularExpressions;

namespace Griddo.Editing;

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

public sealed class GriddoChoiceCellEditor : IGriddoCellEditor
{
    private readonly string[] _choices;
    private readonly bool _allowEmpty;

    public GriddoChoiceCellEditor(IEnumerable<string> choices, bool allowEmpty = true)
    {
        _choices = choices
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _allowEmpty = allowEmpty;
    }

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
        var trimmed = (editBuffer ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            newValue = _allowEmpty ? string.Empty : (_choices.FirstOrDefault() ?? string.Empty);
            return _allowEmpty || _choices.Length > 0;
        }

        var match = _choices.FirstOrDefault(x => string.Equals(x, trimmed, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            newValue = match;
            return true;
        }

        newValue = null;
        return false;
    }
}

public sealed class GriddoOptionsCellEditor : IGriddoOptionsCellEditor
{
    private readonly string[] _options;
    private readonly bool _allowEmpty;

    public GriddoOptionsCellEditor(IEnumerable<string> options, bool allowMultiple = false, bool allowEmpty = true)
    {
        _options = options
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        AllowMultiple = allowMultiple;
        _allowEmpty = allowEmpty;
    }

    public IReadOnlyList<string> Options => _options;
    public bool AllowMultiple { get; }
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
        var values = ParseValues(editBuffer);
        if (values.Count == 0)
        {
            newValue = _allowEmpty ? string.Empty : (_options.FirstOrDefault() ?? string.Empty);
            return _allowEmpty || _options.Length > 0;
        }

        if (!AllowMultiple && values.Count > 1)
        {
            newValue = null;
            return false;
        }

        foreach (var value in values)
        {
            if (!_options.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)))
            {
                newValue = null;
                return false;
            }
        }

        newValue = FormatValues(values);
        return true;
    }

    public IReadOnlyList<string> ParseValues(string editBuffer)
    {
        if (string.IsNullOrWhiteSpace(editBuffer))
        {
            return [];
        }

        var parts = editBuffer
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static x => x.Trim())
            .Where(static x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return parts;
    }

    public string FormatValues(IEnumerable<string> values)
    {
        var list = values
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return string.Join(", ", list);
    }
}

public sealed class GriddoColorExampleCellEditor : IGriddoCellEditor
{
    private static readonly string[] ExampleColors =
    [
        "Black", "White", "Red", "Green", "Blue", "Orange", "Purple", "Gray", "Yellow", "Transparent"
    ];

    private static readonly Regex HexPattern = new(
        "^#([0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$",
        RegexOptions.Compiled);

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
        var trimmed = (editBuffer ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            newValue = string.Empty;
            return true;
        }

        if (ExampleColors.Any(x => string.Equals(x, trimmed, StringComparison.OrdinalIgnoreCase))
            || HexPattern.IsMatch(trimmed)
            || trimmed.StartsWith("sc#", StringComparison.OrdinalIgnoreCase))
        {
            newValue = trimmed;
            return true;
        }

        newValue = null;
        return false;
    }
}

public sealed class GriddoDialogLauncherCellEditor : IGriddoDialogButtonCellEditor
{
    public string ButtonText => "...";
    public string LaunchToken => "...";

    public bool CanStartWith(char inputChar) => false;

    public string BeginEdit(object? currentValue, char? firstCharacter = null)
        => currentValue?.ToString() ?? string.Empty;

    public bool TryCommit(string editBuffer, out object? newValue)
    {
        newValue = string.IsNullOrWhiteSpace(editBuffer) ? LaunchToken : editBuffer.Trim();
        return true;
    }
}

public sealed class GriddoKnownColorCellEditor : IGriddoSwatchOptionsCellEditor
{
    private static readonly string[] KnownColorOptions = BuildKnownColorOptions();

    public IReadOnlyList<string> Options => KnownColorOptions;
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
        var value = (editBuffer ?? string.Empty).Trim();
        if (value.Length == 0 || string.Equals(value, "(default)", StringComparison.OrdinalIgnoreCase))
        {
            newValue = string.Empty;
            return true;
        }

        var match = KnownColorOptions.FirstOrDefault(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            newValue = match;
            return true;
        }

        newValue = null;
        return false;
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

    public bool TryGetSwatchBrush(string option, out System.Windows.Media.Brush brush)
    {
        brush = System.Windows.Media.Brushes.Transparent;
        if (string.IsNullOrWhiteSpace(option) || string.Equals(option, "(default)", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var converted = new System.Windows.Media.BrushConverter().ConvertFromString(option);
            if (converted is System.Windows.Media.Brush b)
            {
                brush = b;
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static string[] BuildKnownColorOptions()
    {
        var names = typeof(System.Windows.Media.Colors)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(static p => p.Name)
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        names.Insert(0, "(default)");
        return names.ToArray();
    }
}

public sealed class GriddoFormatStringCellEditor : IGriddoContextualOptionsCellEditor
{
    private static readonly string[] NumberFormatOptions =
    [
        "(none)",
        "N0", "N2", "N3",
        "F0", "F2", "F3",
        "G", "G3", "G5",
        "P0", "P2",
        "C0", "C2",
        "#,##0", "#,##0.00", "0.###", "0.00"
    ];

    private static readonly string[] DateTimeFormatOptions =
    [
        "(none)",
        "yyyy-MM-dd",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd HH:mm:ss",
        "MM/dd/yyyy",
        "dd.MM.yyyy",
        "HH:mm",
        "HH:mm:ss",
        "g", "G", "d", "D", "t", "T", "o", "s"
    ];

    public IReadOnlyList<string> Options => NumberFormatOptions;
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
        var value = (editBuffer ?? string.Empty).Trim();
        if (value.Length == 0 || string.Equals(value, "(none)", StringComparison.OrdinalIgnoreCase))
        {
            newValue = string.Empty;
            return true;
        }

        newValue = value;
        return true;
    }

    public IReadOnlyList<string> GetOptions(object? recordSource)
    {
        var t = recordSource?.GetType();
        var isNumeric = t?.GetProperty("IsNumericProperty")?.GetValue(recordSource) as bool? == true;
        var isDateTime = t?.GetProperty("IsDateTimeProperty")?.GetValue(recordSource) as bool? == true;
        if (isDateTime && !isNumeric)
        {
            return DateTimeFormatOptions;
        }

        if (isNumeric && !isDateTime)
        {
            return NumberFormatOptions;
        }

        return NumberFormatOptions;
    }

    public bool TryGetOptionExample(object? recordSource, string option, out string example)
    {
        example = string.Empty;
        if (string.IsNullOrWhiteSpace(option) || string.Equals(option, "(none)", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var formats = GetOptions(recordSource);
        var isDateMode = formats == DateTimeFormatOptions;
        try
        {
            example = isDateMode
                ? DateTime.Now.ToString(option, CultureInfo.CurrentCulture)
                : 12345.6789.ToString(option, CultureInfo.CurrentCulture);
            return !string.IsNullOrWhiteSpace(example);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public IReadOnlyList<string> ParseValues(string editBuffer)
    {
        var value = (editBuffer ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return ["(none)"];
        }

        return [value];
    }

    public string FormatValues(IEnumerable<string> values)
    {
        var first = values.FirstOrDefault(static x => !string.IsNullOrWhiteSpace(x))?.Trim();
        return string.Equals(first, "(none)", StringComparison.OrdinalIgnoreCase) ? string.Empty : (first ?? string.Empty);
    }
}

public static class GriddoCellEditors
{
    public static readonly IGriddoCellEditor Text = new GriddoTextCellEditor();
    public static readonly IGriddoCellEditor Number = new GriddoNumberCellEditor();
    public static readonly IGriddoCellEditor Bool = new GriddoBoolCellEditor();
    public static readonly IGriddoCellEditor FontStyle = new GriddoChoiceCellEditor(["normal", "italic", "oblique"]);
    public static readonly IGriddoCellEditor ExampleColor = new GriddoColorExampleCellEditor();
    public static readonly IGriddoCellEditor FontStyleOptions = new GriddoOptionsCellEditor(["Italic", "Bold", "Underline"], allowMultiple: true);
    public static readonly IGriddoCellEditor FontFamilyOptions = new GriddoOptionsCellEditor(
        ["Segoe UI", "Arial", "Calibri", "Consolas", "Times New Roman", "Tahoma", "Verdana", "Courier New"],
        allowMultiple: false);
    public static readonly IGriddoCellEditor ExampleColorOptions = new GriddoOptionsCellEditor(
        ["Black", "White", "Red", "Green", "Blue", "Orange", "Purple", "Gray", "Yellow", "Transparent"],
        allowMultiple: false);
    public static readonly IGriddoCellEditor DialogLauncher = new GriddoDialogLauncherCellEditor();
    public static readonly IGriddoCellEditor KnownColorsDropdown = new GriddoKnownColorCellEditor();
    public static readonly IGriddoCellEditor FormatStringOptions = new GriddoFormatStringCellEditor();
    public static readonly IGriddoCellEditor TextAlignmentOptions = new GriddoOptionsCellEditor(
        ["Left", "Center", "Right"],
        allowMultiple: false,
        allowEmpty: false);
    public static readonly IGriddoCellEditor FieldFillOptions = new GriddoOptionsCellEditor(
        ["None", "1", "2", "3"],
        allowMultiple: false,
        allowEmpty: false);
}
