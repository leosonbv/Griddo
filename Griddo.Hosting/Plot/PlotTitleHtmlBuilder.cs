using System.Globalization;
using System.Net;
using Griddo.Fields;
using Griddo.Hosting.Configuration;

namespace Griddo.Hosting.Plot;

internal static class PlotTitleHtmlBuilder
{
    public static string BuildTitleHtml(
        object? recordSource,
        Func<IReadOnlyList<IGriddoFieldView>>? allFieldsAccessor,
        IReadOnlyList<PlotTitleSegmentConfiguration> titleSegments)
    {
        if (recordSource is null || allFieldsAccessor is null)
        {
            return string.Empty;
        }

        var allFields = allFieldsAccessor();
        var segments = titleSegments.Where(static s => s.Enabled).ToList();
        if (segments.Count == 0)
        {
            return string.Empty;
        }

        var rows = new List<string>(segments.Count);
        foreach (var segment in segments)
        {
            if (segment.SourceFieldIndex < 0 || segment.SourceFieldIndex >= allFields.Count)
            {
                continue;
            }

            var field = allFields[segment.SourceFieldIndex];
            var label = string.IsNullOrWhiteSpace(segment.AbbreviatedHeaderOverride)
                ? (field.Header ?? string.Empty)
                : segment.AbbreviatedHeaderOverride;
            var value = field.GetValue(recordSource);
            var rendered = field.IsHtml
                ? (value?.ToString() ?? string.Empty)
                : BuildStyledText(WebUtility.HtmlEncode(field.FormatValue(value)), field);
            if (!segment.WordWrap)
            {
                rendered = rendered.Replace(" ", "\u00A0", StringComparison.Ordinal);
            }

            var breakBefore = segment.AddLineBreakAfter ? " data-break-before='1'" : string.Empty;
            rows.Add($"<tr{breakBefore}><td><b>{WebUtility.HtmlEncode(label)}</b></td><td>{rendered}</td></tr>");
        }

        return rows.Count == 0
            ? string.Empty
            : "<table style='border-collapse:collapse;border:none'><tbody>"
              + string.Join(string.Empty, rows)
              + "</tbody></table>";
    }

    private static string BuildStyledText(string text, IGriddoFieldView field)
    {
        var styles = new List<string>();
        if (field is IGriddoFieldColorView colorView)
        {
            if (!string.IsNullOrWhiteSpace(colorView.ForegroundColor))
            {
                styles.Add($"color:{WebUtility.HtmlEncode(colorView.ForegroundColor)}");
            }

            if (!string.IsNullOrWhiteSpace(colorView.BackgroundColor))
            {
                styles.Add($"background-color:{WebUtility.HtmlEncode(colorView.BackgroundColor)}");
            }
        }

        if (field is IGriddoFieldFontView fontView)
        {
            if (!string.IsNullOrWhiteSpace(fontView.FontFamilyName))
            {
                styles.Add($"font-family:{WebUtility.HtmlEncode(fontView.FontFamilyName)}");
            }

            if (fontView.FontSize > 0)
            {
                styles.Add($"font-size:{fontView.FontSize.ToString(CultureInfo.InvariantCulture)}px");
            }

            if (!string.IsNullOrWhiteSpace(fontView.FontStyleName))
            {
                styles.Add($"font-style:{WebUtility.HtmlEncode(fontView.FontStyleName)}");
            }
        }

        return styles.Count == 0
            ? text
            : $"<span style='{string.Join(";", styles)}'>{text}</span>";
    }
}
