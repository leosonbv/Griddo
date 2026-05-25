using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Griddo.Abstractions.Fields;
using Griddo.Hosting.Contracts;
using Griddo.Hosting.Configuration;

namespace Griddo.Hosting.Plot;

internal readonly record struct PreformattedSegmentValue(string Text);

/// <summary>
/// Caches resolved <c>(field, displayField)</c> bindings per <c>(resolveFields, hostingFields, segments)</c> triplet.
/// Field lists and segment lists are stable at runtime, so resolution is paid exactly once per unique combination.
/// </summary>
internal static class SegmentBindingCache
{
    // Outer key: resolveFields instance. Inner key: (hostingFields, segments) tuple.
    private static readonly ConditionalWeakTable<IReadOnlyList<IGriddoFieldView>,
        Dictionary<(IReadOnlyList<IGriddoFieldView>, IReadOnlyList<PlotTitleSegmentConfiguration>),
            (IGriddoFieldView field, IGriddoFieldView displayField)?[]>> _cache = new();

    /// <summary>
    /// Returns a cached array of <c>(field, displayField)?</c>, one entry per segment in <paramref name="segments"/>.
    /// A <c>null</c> entry means the segment could not be resolved.
    /// </summary>
    public static (IGriddoFieldView field, IGriddoFieldView displayField)?[] Get(
        IReadOnlyList<IGriddoFieldView> resolveFields,
        IReadOnlyList<IGriddoFieldView> hostingFields,
        IReadOnlyList<PlotTitleSegmentConfiguration> segments)
    {
        var inner = _cache.GetOrCreateValue(resolveFields);
        var key = (hostingFields, segments);
        lock (inner)
        {
            if (!inner.TryGetValue(key, out var bindings))
            {
                bindings = HostingSegmentFieldResolver.ResolveAll(segments, resolveFields, hostingFields);
                inner[key] = bindings;
            }

            return bindings;
        }
    }
}

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
        var bindings = SegmentBindingCache.Get(allFields, allFields, titleSegments);
        return BuildComposedHtml(titleSegments, bindings, i => bindings[i]?.field.GetValue(recordSource));
    }

    /// <summary>
    /// Fast overload: caller supplies pre-resolved <paramref name="bindings"/> from its own cache field —
    /// no dictionary lookup, no allocation.
    /// </summary>
    internal static string BuildTitleHtml(
        object? recordSource,
        IReadOnlyList<PlotTitleSegmentConfiguration> titleSegments,
        (IGriddoFieldView field, IGriddoFieldView displayField)?[] bindings)
    {
        if (recordSource is null)
        {
            return string.Empty;
        }

        return BuildComposedHtml(titleSegments, bindings, i => bindings[i]?.field.GetValue(recordSource));
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
        var bindings = SegmentBindingCache.Get(resolveFields, hostingFields, titleSegments);

        return BuildComposedHtml(titleSegments, bindings, i =>
        {
            var b = bindings[i];
            if (b is null) return null;
            var field = b.Value.field;
            var seg = titleSegments[i];
            var plainOverride = signalProvider.TryGetCalibrationPointSegmentPlainValue(recordSource, pointIndex, seg, field);
            if (plainOverride != null) return new PreformattedSegmentValue(plainOverride);
            return pointRow is null ? null : field.GetValue(pointRow);
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
        var bindings = SegmentBindingCache.Get(allFields, allFields, segments);
        return BuildComposedPlainText(segments, bindings, i => bindings[i]?.field.GetValue(recordSource));
    }

    public static string BuildPeakLabelPlainText(
        object? labelRecord,
        Func<IReadOnlyList<IGriddoFieldView>>? allFieldsAccessor,
        IReadOnlyList<PlotTitleSegmentConfiguration> segments,
        Func<IReadOnlyList<IGriddoFieldView>>? labelFieldsAccessor = null)
    {
        if (labelRecord is null || allFieldsAccessor is null)
        {
            return string.Empty;
        }

        var hostingFields = allFieldsAccessor();
        var resolveFields = labelFieldsAccessor?.Invoke() ?? hostingFields;
        var bindings = SegmentBindingCache.Get(resolveFields, hostingFields, segments);
        return BuildComposedPlainText(segments, bindings, i => bindings[i]?.field.GetValue(labelRecord));
    }

    /// <summary>
    /// Fast overload: caller supplies pre-resolved <paramref name="bindings"/> from its own cache field.
    /// </summary>
    internal static string BuildPeakLabelPlainText(
        object? labelRecord,
        IReadOnlyList<PlotTitleSegmentConfiguration> segments,
        (IGriddoFieldView field, IGriddoFieldView displayField)?[] bindings)
    {
        if (labelRecord is null)
        {
            return string.Empty;
        }

        return BuildComposedPlainText(segments, bindings, i => bindings[i]?.field.GetValue(labelRecord));
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
        var bindings = SegmentBindingCache.Get(resolveFields, hostingFields, segments);

        return BuildComposedPlainText(segments, bindings, i =>
        {
            var b = bindings[i];
            if (b is null) return null;
            var field = b.Value.field;
            var seg = segments[i];
            var plainOverride = signalProvider.TryGetCalibrationPointSegmentPlainValue(recordSource, pointIndex, seg, field);
            if (plainOverride != null) return new PreformattedSegmentValue(plainOverride);
            return pointRow is null ? null : field.GetValue(pointRow);
        });
    }

    /// <summary>
    /// Fast overload: caller supplies pre-resolved <paramref name="bindings"/> from its own cache field.
    /// </summary>
    internal static string BuildCalibrationPointLabelPlainText(
        object? recordSource,
        int pointIndex,
        IReadOnlyList<PlotTitleSegmentConfiguration> segments,
        (IGriddoFieldView field, IGriddoFieldView displayField)?[] bindings,
        ICalibrationSignalProvider signalProvider)
    {
        if (recordSource is null)
        {
            return string.Empty;
        }

        var pointRow = signalProvider.TryGetCalibrationPointLabelRecord(recordSource, pointIndex);
        return BuildComposedPlainText(segments, bindings, i =>
        {
            var b = bindings[i];
            if (b is null) return null;
            var field = b.Value.field;
            var seg = segments[i];
            var plainOverride = signalProvider.TryGetCalibrationPointSegmentPlainValue(recordSource, pointIndex, seg, field);
            if (plainOverride != null) return new PreformattedSegmentValue(plainOverride);
            return pointRow is null ? null : field.GetValue(pointRow);
        });
    }

    private static string BuildComposedHtml(
        IReadOnlyList<PlotTitleSegmentConfiguration> segments,
        (IGriddoFieldView field, IGriddoFieldView displayField)?[] bindings,
        Func<int, object?> getValue)
    {
        var tableRows = new List<string>();
        var rowCells = new List<string>();
        var isFirstTableRow = true;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (!segment.Enabled)
            {
                continue;
            }

            var b = bindings[i];
            if (b is null)
            {
                continue;
            }

            if (!TryBuildTableCells(segment, b.Value.field, b.Value.displayField, getValue(i), out var cells))
            {
                continue;
            }

            rowCells.AddRange(cells);

            if (segment.AddLineBreakAfter)
            {
                tableRows.Add(FormatTitleTableRow(rowCells, breakBefore: !isFirstTableRow));
                isFirstTableRow = false;
                rowCells.Clear();
            }
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
        (IGriddoFieldView field, IGriddoFieldView displayField)?[] bindings,
        Func<int, object?> getValue)
    {
        var lines = new List<string>();
        var lineParts = new List<string>();
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (!segment.Enabled)
            {
                continue;
            }

            var b = bindings[i];
            if (b is null)
            {
                continue;
            }

            if (!TryBuildPlainPart(segment, b.Value.field, b.Value.displayField, getValue(i), out var part))
            {
                continue;
            }

            lineParts.Add(part);

            if (segment.AddLineBreakAfter)
            {
                lines.Add(string.Join(PairSeparatorPlain, lineParts));
                lineParts.Clear();
            }
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
