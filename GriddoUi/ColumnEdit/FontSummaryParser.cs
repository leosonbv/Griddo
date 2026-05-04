using System.Globalization;
using System.Text.RegularExpressions;

namespace GriddoUi.ColumnEdit;

internal static class FontSummaryParser
{
    private static readonly Regex SizeSegment = new(
        @",\s*(default|\d+(?:\.\d+)?),\s*",
        RegexOptions.RightToLeft | RegexOptions.CultureInvariant);

    /// <summary>Parses the font summary line (family, size, style, Fg) back onto <paramref name="row"/>.</summary>
    public static bool TryApplyFontSummaryText(ColumnEditRow row, string text)
    {
        var t = text.Trim();
        if (t.Length == 0)
        {
            row.FontFamilyName = string.Empty;
            row.FontSize = 0;
            row.FontStyleName = string.Empty;
            row.ForegroundColor = string.Empty;
            return true;
        }

        string fg = string.Empty;
        var work = t;
        const string fgMarker = ", Fg:";
        var fi = work.LastIndexOf(fgMarker, StringComparison.OrdinalIgnoreCase);
        if (fi >= 0)
        {
            fg = work[(fi + fgMarker.Length)..].Trim();
            work = work[..fi].TrimEnd().TrimEnd(',');
        }

        var m = SizeSegment.Match(work);
        if (!m.Success)
        {
            return false;
        }

        var sizeToken = m.Groups[1].Value;
        var family = work[..m.Index].TrimEnd(',').Trim();
        var style = work[(m.Index + m.Length)..].Trim();

        if (string.Equals(sizeToken, "default", StringComparison.OrdinalIgnoreCase))
        {
            row.FontSize = 0;
        }
        else if (double.TryParse(sizeToken, NumberStyles.Any, CultureInfo.InvariantCulture, out var sz))
        {
            row.FontSize = Math.Max(0, sz);
        }
        else
        {
            return false;
        }

        row.FontFamilyName = string.Equals(family, "(default)", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : family;
        row.FontStyleName = string.Equals(style, "Regular", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : style;

        row.ForegroundColor = string.IsNullOrWhiteSpace(fg) || string.Equals(fg, "(default)", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : fg.Trim();

        return true;
    }
}
