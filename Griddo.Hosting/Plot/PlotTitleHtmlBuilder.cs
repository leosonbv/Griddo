using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Griddo.Fields;
using Griddo.Hosting.Abstractions;
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
            var sourceFieldIndex = HostingSegmentFieldResolver.Resolve(
                allFields,
                segment.SourceObjectName,
                segment.PropertyName,
                segment.SourceFieldKey,
                segment.SourceFieldIndex);
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

    /// <summary>
    /// Builds the same HTML table as <see cref="BuildTitleHtml"/>, but optional per-point plain values come from
    /// <paramref name="signalProvider"/> (calibration bracket level). Styled like the resolved grid field.
    /// </summary>
    public static string BuildCalibrationPointLabelHtml(
        object? recordSource,
        int pointIndex,
        Func<IReadOnlyList<IGriddoFieldView>>? allFieldsAccessor,
        IReadOnlyList<PlotTitleSegmentConfiguration> titleSegments,
        ICalibrationSignalProvider signalProvider,
        Func<IReadOnlyList<IGriddoFieldView>>? calibrationLabelFieldsAccessor = null)
    {
        if (recordSource is null || allFieldsAccessor is null)
        {
            return string.Empty;
        }

        var resolveFields = calibrationLabelFieldsAccessor?.Invoke() ?? allFieldsAccessor();
        var segments = titleSegments.Where(static s => s.Enabled).ToList();
        if (segments.Count == 0)
        {
            return string.Empty;
        }

        var pointRow = signalProvider.TryGetCalibrationPointLabelRecord(recordSource, pointIndex);

        var rows = new List<string>(segments.Count);
        var rowCells = new List<string>(segments.Count * 2);
        foreach (var segment in segments)
        {
            var sourceFieldIndex = HostingSegmentFieldResolver.Resolve(
                resolveFields,
                segment.SourceObjectName,
                segment.PropertyName,
                segment.SourceFieldKey,
                segment.SourceFieldIndex);
            if (sourceFieldIndex < 0 || sourceFieldIndex >= resolveFields.Count)
            {
                continue;
            }

            var field = resolveFields[sourceFieldIndex];
            var valueOnlyLayout = segment.OmitLabelColumn;
            var label = valueOnlyLayout
                ? string.Empty
                : (string.IsNullOrWhiteSpace(segment.AbbreviatedHeaderOverride)
                    ? (field.Header ?? string.Empty)
                    : segment.AbbreviatedHeaderOverride);

            var plainOverride = signalProvider.TryGetCalibrationPointSegmentPlainValue(
                recordSource,
                pointIndex,
                segment,
                field);
            string rendered;
            if (plainOverride != null)
            {
                rendered = BuildStyledText(WebUtility.HtmlEncode(plainOverride), field);
            }
            else if (pointRow is null)
            {
                rendered = string.Empty;
            }
            else if (field.IsHtml)
            {
                rendered = field.GetValue(pointRow)?.ToString() ?? string.Empty;
            }
            else
            {
                var value = field.GetValue(pointRow);
                rendered = BuildStyledText(WebUtility.HtmlEncode(field.FormatValue(value)), field);
            }

            if (!segment.WordWrap)
            {
                rendered = rendered.Replace(" ", "\u00A0", StringComparison.Ordinal);
            }

            if (valueOnlyLayout)
            {
                rowCells.Add($"<td colspan=\"2\">{rendered}</td>");
            }
            else
            {
                rowCells.Add($"<td><b>{WebUtility.HtmlEncode(label)}</b></td><td>{rendered}</td>");
            }

            if (segment.AddLineBreakAfter)
            {
                rows.Add("<tr>" + string.Join(string.Empty, rowCells) + "</tr>");
                rowCells.Clear();
            }
        }

        if (rowCells.Count > 0)
        {
            rows.Add("<tr>" + string.Join(string.Empty, rowCells) + "</tr>");
        }

        return rows.Count == 0
            ? string.Empty
            : "<table style='border-collapse:collapse;border:none'><tbody>"
              + string.Join(string.Empty, rows)
              + "</tbody></table>";
    }

    private static readonly Regex HtmlTableRowRegex = new("<tr[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex HtmlTableCellRegex = new("<td[^>]*>(.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex HtmlTagStripRegex = new("<[^>]+>", RegexOptions.Compiled);

    /// <summary>Flattens plot title HTML (especially table rows) to newline-separated plain text for Skia overlays.</summary>
    public static string HtmlTableToPlainSummary(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        if (!html.Contains("<tr", StringComparison.OrdinalIgnoreCase))
        {
            return HtmlDecodeStripTags(html);
        }

        var rows = HtmlTableRowRegex.Matches(html);
        if (rows.Count == 0)
        {
            return HtmlDecodeStripTags(html);
        }

        var lines = new List<string>();
        foreach (Match row in rows)
        {
            var rowHtml = row.Groups[1].Value;
            var cells = HtmlTableCellRegex.Matches(rowHtml);
            if (cells.Count >= 2)
            {
                var parts = new List<string>();
                for (var c = 0; c + 1 < cells.Count; c += 2)
                {
                    var h = HtmlDecodeStripTags(cells[c].Groups[1].Value).Trim();
                    var v = HtmlDecodeStripTags(cells[c + 1].Groups[1].Value).Trim();
                    if (string.IsNullOrWhiteSpace(h) && string.IsNullOrWhiteSpace(v))
                    {
                        continue;
                    }

                    parts.Add(string.IsNullOrWhiteSpace(h) ? v : $"{h}: {v}");
                }

                if (parts.Count > 0)
                {
                    lines.Add(string.Join(' ', parts));
                }
            }
            else if (cells.Count == 1)
            {
                var t = HtmlDecodeStripTags(cells[0].Groups[1].Value).Trim();
                if (!string.IsNullOrWhiteSpace(t))
                {
                    lines.Add(t);
                }
            }
        }

        return lines.Count == 0 ? HtmlDecodeStripTags(html) : string.Join('\n', lines);
    }

    private static string HtmlDecodeStripTags(string html)
    {
        var noTags = HtmlTagStripRegex.Replace(html, string.Empty);
        return WebUtility.HtmlDecode(noTags)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }
}
