using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Griddo.Fields;
using Griddo.Hosting.Abstractions;
using Griddo.Hosting.Configuration;

namespace Griddo.Hosting.Plot;

internal readonly record struct PreformattedSegmentValue(string Text);

internal static class PlotTitleHtmlBuilder
{
    private const string PairSeparatorHtml = " &middot; ";
    private const string PairSeparatorPlain = " · ";

    private static readonly Regex HtmlTagStripRegex = new("<[^>]+>", RegexOptions.Compiled);

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
        return BuildComposedHtml(
            titleSegments,
            segment =>
            {
                var resolved = ResolveSegment(segment, allFields, allFields);
                if (resolved is null)
                {
                    return null;
                }

                var (field, displayField) = resolved.Value;
                return (field, displayField, () => field.GetValue(recordSource));
            });
    }

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

        var hostingFields = allFieldsAccessor();
        var resolveFields = calibrationLabelFieldsAccessor?.Invoke() ?? hostingFields;
        var pointRow = signalProvider.TryGetCalibrationPointLabelRecord(recordSource, pointIndex);

        return BuildComposedHtml(
            titleSegments,
            segment =>
            {
                var resolved = ResolveSegment(segment, resolveFields, hostingFields);
                if (resolved is null)
                {
                    return null;
                }

                var (field, displayField) = resolved.Value;
                return (field, displayField, () =>
                {
                    var plainOverride = signalProvider.TryGetCalibrationPointSegmentPlainValue(
                        recordSource,
                        pointIndex,
                        segment,
                        field);
                    if (plainOverride != null)
                    {
                        return new PreformattedSegmentValue(plainOverride);
                    }

                    if (pointRow is null)
                    {
                        return null;
                    }

                    return field.GetValue(pointRow);
                });
            });
    }

    public static string BuildRecordLabelPlainText(
        object? recordSource,
        Func<IReadOnlyList<IGriddoFieldView>>? allFieldsAccessor,
        IReadOnlyList<PlotTitleSegmentConfiguration> segments)
    {
        if (recordSource is null || allFieldsAccessor is null)
        {
            return string.Empty;
        }

        var allFields = allFieldsAccessor();
        return BuildComposedPlainText(
            segments,
            segment =>
            {
                var resolved = ResolveSegment(segment, allFields, allFields);
                if (resolved is null)
                {
                    return null;
                }

                var (field, displayField) = resolved.Value;
                return (field, displayField, () => field.GetValue(recordSource));
            });
    }

    public static string BuildCalibrationPointLabelPlainText(
        object? recordSource,
        int pointIndex,
        Func<IReadOnlyList<IGriddoFieldView>>? allFieldsAccessor,
        IReadOnlyList<PlotTitleSegmentConfiguration> segments,
        ICalibrationSignalProvider signalProvider,
        Func<IReadOnlyList<IGriddoFieldView>>? calibrationLabelFieldsAccessor = null)
    {
        if (recordSource is null || allFieldsAccessor is null)
        {
            return string.Empty;
        }

        var hostingFields = allFieldsAccessor();
        var resolveFields = calibrationLabelFieldsAccessor?.Invoke() ?? hostingFields;
        var pointRow = signalProvider.TryGetCalibrationPointLabelRecord(recordSource, pointIndex);

        return BuildComposedPlainText(
            segments,
            segment =>
            {
                var resolved = ResolveSegment(segment, resolveFields, hostingFields);
                if (resolved is null)
                {
                    return null;
                }

                var (field, displayField) = resolved.Value;
                return (field, displayField, () =>
                {
                    var plainOverride = signalProvider.TryGetCalibrationPointSegmentPlainValue(
                        recordSource,
                        pointIndex,
                        segment,
                        field);
                    if (plainOverride != null)
                    {
                        return new PreformattedSegmentValue(plainOverride);
                    }

                    if (pointRow is null)
                    {
                        return null;
                    }

                    return field.GetValue(pointRow);
                });
            });
    }

    private static (IGriddoFieldView field, IGriddoFieldView displayField)? ResolveSegment(
        PlotTitleSegmentConfiguration segment,
        IReadOnlyList<IGriddoFieldView> resolveFields,
        IReadOnlyList<IGriddoFieldView> hostingFields)
    {
        var sourceFieldIndex = HostingSegmentFieldResolver.Resolve(
            resolveFields,
            segment.SourceObjectName,
            segment.PropertyName,
            segment.SourceFieldKey,
            segment.SourceFieldIndex);
        if (sourceFieldIndex < 0 || sourceFieldIndex >= resolveFields.Count)
        {
            return null;
        }

        var field = resolveFields[sourceFieldIndex];
        var hostingFieldIndex = HostingSegmentFieldResolver.Resolve(
            hostingFields,
            segment.SourceObjectName,
            segment.PropertyName,
            segment.SourceFieldKey,
            segment.SourceFieldIndex);
        var displayField = hostingFieldIndex >= 0 && hostingFieldIndex < hostingFields.Count
            ? hostingFields[hostingFieldIndex]
            : field;
        return (field, displayField);
    }

    private static string BuildComposedHtml(
        IReadOnlyList<PlotTitleSegmentConfiguration> segments,
        Func<PlotTitleSegmentConfiguration, (IGriddoFieldView field, IGriddoFieldView displayField, Func<object?> getValue)?> resolveSegment)
    {
        var enabled = segments.Where(static s => s.Enabled).ToList();
        if (enabled.Count == 0)
        {
            return string.Empty;
        }

        var tableRows = new List<string>();
        var rowCells = new List<string>();
        var isFirstTableRow = true;
        foreach (var segment in enabled)
        {
            var resolved = resolveSegment(segment);
            if (resolved is null)
            {
                continue;
            }

            var (field, displayField, getValue) = resolved.Value;
            if (!TryBuildTableCells(segment, field, displayField, getValue(), out var cells))
            {
                continue;
            }

            if (segment.AddLineBreakAfter && rowCells.Count > 0)
            {
                tableRows.Add(FormatTitleTableRow(rowCells, breakBefore: !isFirstTableRow));
                isFirstTableRow = false;
                rowCells.Clear();
            }

            rowCells.AddRange(cells);
        }

        if (rowCells.Count > 0)
        {
            tableRows.Add(FormatTitleTableRow(rowCells, breakBefore: !isFirstTableRow));
        }

        return tableRows.Count == 0
            ? string.Empty
            : "<table style='border-collapse:collapse;border:none'><tbody>"
              + string.Join(string.Empty, tableRows)
              + "</tbody></table>";
    }

    private static string FormatTitleTableRow(IReadOnlyList<string> cells, bool breakBefore) =>
        breakBefore
            ? $"<tr data-break-before='1'>{string.Join(string.Empty, cells)}</tr>"
            : $"<tr>{string.Join(string.Empty, cells)}</tr>";

    private static string BuildComposedPlainText(
        IReadOnlyList<PlotTitleSegmentConfiguration> segments,
        Func<PlotTitleSegmentConfiguration, (IGriddoFieldView field, IGriddoFieldView displayField, Func<object?> getValue)?> resolveSegment)
    {
        var enabled = segments.Where(static s => s.Enabled).ToList();
        if (enabled.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<string>();
        var lineParts = new List<string>();
        foreach (var segment in enabled)
        {
            var resolved = resolveSegment(segment);
            if (resolved is null)
            {
                continue;
            }

            var (field, displayField, getValue) = resolved.Value;
            if (!TryBuildPlainPart(segment, field, displayField, getValue(), out var part))
            {
                continue;
            }

            if (segment.AddLineBreakAfter && lineParts.Count > 0)
            {
                lines.Add(string.Join(PairSeparatorPlain, lineParts));
                lineParts.Clear();
            }

            lineParts.Add(part);
        }

        if (lineParts.Count > 0)
        {
            lines.Add(string.Join(PairSeparatorPlain, lineParts));
        }

        return lines.Count == 0 ? string.Empty : string.Join('\n', lines);
    }

    private static bool TryBuildTableCells(
        PlotTitleSegmentConfiguration segment,
        IGriddoFieldView field,
        IGriddoFieldView displayField,
        object? value,
        out List<string> cells)
    {
        cells = [];
        if (!TryBuildRenderedValue(segment, field, displayField, value, out var rendered))
        {
            return false;
        }

        var header = segment.Header.Trim();
        if (string.IsNullOrWhiteSpace(header))
        {
            cells.Add($"<td colspan=\"2\">{rendered}</td>");
        }
        else
        {
            cells.Add($"<td><b>{WebUtility.HtmlEncode(header)}</b></td>");
            cells.Add($"<td>{rendered}</td>");
        }

        return true;
    }

    private static bool TryBuildRenderedValue(
        PlotTitleSegmentConfiguration segment,
        IGriddoFieldView field,
        IGriddoFieldView displayField,
        object? value,
        out string rendered)
    {
        rendered = string.Empty;
        if (value is PreformattedSegmentValue preformatted)
        {
            rendered = BuildStyledText(WebUtility.HtmlEncode(preformatted.Text), displayField);
        }
        else if (field.IsHtml)
        {
            rendered = value?.ToString() ?? string.Empty;
        }
        else
        {
            rendered = BuildStyledText(
                WebUtility.HtmlEncode(FormatSegmentValue(value, displayField, segment.FormatString)),
                displayField);
        }

        return !string.IsNullOrWhiteSpace(rendered);
    }

    private static bool TryBuildPlainPart(
        PlotTitleSegmentConfiguration segment,
        IGriddoFieldView field,
        IGriddoFieldView displayField,
        object? value,
        out string part)
    {
        part = string.Empty;
        string rendered;
        if (value is PreformattedSegmentValue preformatted)
        {
            rendered = preformatted.Text;
        }
        else if (field.IsHtml)
        {
            rendered = HtmlDecodeStripTags(value?.ToString() ?? string.Empty);
        }
        else
        {
            rendered = FormatSegmentValue(value, displayField, segment.FormatString);
        }

        if (string.IsNullOrWhiteSpace(rendered))
        {
            return false;
        }

        part = FormatHeaderValuePair(segment.Header, rendered.Trim());
        return !string.IsNullOrWhiteSpace(part);
    }

    private static string FormatHeaderValuePair(string header, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmedHeader = header.Trim();
        return string.IsNullOrWhiteSpace(trimmedHeader)
            ? value
            : $"{trimmedHeader}: {value}";
    }

    private static string FormatSegmentValue(object? value, IGriddoFieldView field, string? segmentFormatString)
    {
        if (!string.IsNullOrWhiteSpace(segmentFormatString))
        {
            if (value is null or DBNull)
            {
                return string.Empty;
            }

            if (value is IFormattable formattable)
            {
                return formattable.ToString(segmentFormatString, CultureInfo.CurrentCulture) ?? string.Empty;
            }

            return value.ToString() ?? string.Empty;
        }

        return field.FormatValue(value);
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

    private static string HtmlDecodeStripTags(string html)
    {
        var noTags = HtmlTagStripRegex.Replace(html, string.Empty);
        return WebUtility.HtmlDecode(noTags)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("&middot;", PairSeparatorPlain, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }
}
