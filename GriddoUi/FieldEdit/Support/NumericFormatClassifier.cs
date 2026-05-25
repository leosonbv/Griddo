using System;

namespace GriddoUi.FieldEdit.Support;

/// <summary>
/// SRP collaborator: classifies format strings as numeric for alignment and editor decisions.
/// Extracted from MetadataBuilder to keep that class focused on record building.
/// </summary>
internal static class NumericFormatClassifier
{
    public static bool LooksLikeNumericFormatString(string? formatString)
    {
        if (string.IsNullOrWhiteSpace(formatString))
        {
            return false;
        }

        var fmt = formatString.Trim();
        if (fmt.Length == 0)
        {
            return false;
        }

        if (IsStandardNumericFormatString(fmt))
        {
            return true;
        }

        var hasDigitPlaceholder = false;
        foreach (var ch in fmt)
        {
            switch (ch)
            {
                case '0':
                case '#':
                    hasDigitPlaceholder = true;
                    break;
                case '%':
                case 'E':
                case 'e':
                    return true;
            }
        }

        return hasDigitPlaceholder
            && fmt.IndexOfAny(['0', '#', '.', ',', '%', 'E', 'e']) >= 0;
    }

    private static bool IsStandardNumericFormatString(string format)
    {
        if (format.Length == 0)
        {
            return false;
        }

        var index = 0;
        switch (char.ToUpperInvariant(format[index]))
        {
            case 'C':
            case 'D':
            case 'E':
            case 'F':
            case 'G':
            case 'N':
            case 'P':
            case 'R':
            case 'X':
                index++;
                break;
            default:
                return false;
        }

        while (index < format.Length && char.IsDigit(format[index]))
        {
            index++;
        }

        return index == format.Length;
    }
}
