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
            var sourceFieldIndex = ResolveSourceFieldIndex(allFields, segment);
            if (sourceFieldIndex < 0 || sourceFieldIndex >= allFields.Count)
            {
                continue;
            }

            var field = allFields[sourceFieldIndex];
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

            rows.Add($"<tr><td><b>{WebUtility.HtmlEncode(label)}</b></td><td>{rendered}</td></tr>");
        }

        return rows.Count == 0
            ? string.Empty
            : "<table style='border-collapse:collapse;border:none'><tbody>"
              + string.Join(string.Empty, rows)
              + "</tbody></table>";
    }

    private static int ResolveSourceFieldIndex(IReadOnlyList<IGriddoFieldView> allFields, PlotTitleSegmentConfiguration segment)
    {
        if (!string.IsNullOrWhiteSpace(segment.SourceFieldKey))
        {
            for (var i = 0; i < allFields.Count; i++)
            {
                if (string.Equals(ResolveFieldKey(allFields[i], i), segment.SourceFieldKey, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        return segment.SourceFieldIndex;
    }

    private static string ResolveFieldKey(IGriddoFieldView field, int fieldIndex)
    {
        if (field is IGriddoFieldSourceMember sourceMember && !string.IsNullOrWhiteSpace(sourceMember.SourceMemberName))
        {
            return sourceMember.SourceMemberName;
        }

        return !string.IsNullOrWhiteSpace(field.Header) ? field.Header : fieldIndex.ToString(CultureInfo.InvariantCulture);
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
