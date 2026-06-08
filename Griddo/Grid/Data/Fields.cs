using System.Windows.Media;
using System.Windows.Threading;
using Griddo.Abstractions.Fields;
using Griddo.Core.Layout;
using Griddo.Fields;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    private bool _fieldFillWidthCacheValid;
    private int _cachedTotalFieldFillWeight;
    private double _cachedNonFillFieldWidthSum;

    private void InvalidateFieldFillWidthCache() => _fieldFillWidthCacheValid = false;

    private void EnsureFieldFillWidthCache()
    {
        if (_fieldFillWidthCacheValid)
        {
            return;
        }

        var totalFill = 0;
        var nonFill = 0.0;
        for (var i = 0; i < Fields.Count; i++)
        {
            var fillW = GetFieldFillWeight(Fields[i]);
            if (fillW <= 0)
            {
                nonFill += GetFieldBaseWidth(i);
            }
            else
            {
                totalFill += fillW;
            }
        }

        _cachedTotalFieldFillWeight = totalFill;
        _cachedNonFillFieldWidthSum = nonFill;
        _fieldFillWidthCacheValid = true;
    }

    private double GetFieldWidth(int fieldIndex)
    {
        if (fieldIndex >= 0 && fieldIndex < Fields.Count)
        {
            var fillWeight = GetFieldFillWeight(Fields[fieldIndex]);
            if (fillWeight > 0)
            {
                return GetWeightedFillFieldWidth(fillWeight);
            }
        }

        return GetFieldBaseWidth(fieldIndex);
    }

    private static int GetFieldFillWeight(IGriddoFieldView field) =>
        field.FieldFill <= 0 ? 0 : Math.Min(field.FieldFill, 3);

    private double GetWeightedFillFieldWidth(int fillWeight)
    {
        EnsureFieldFillWidthCache();
        var totalFillWeight = _cachedTotalFieldFillWeight;
        if (totalFillWeight <= 0)
        {
            return MinFieldWidth * ContentScale;
        }

        var nonFillWidth = _cachedNonFillFieldWidthSum;

        var viewportAlongFieldAxis = IsBodyTransposed ? _viewportBodyHeight : _viewportBodyWidth;
        return FieldWidthService.ResolveWeightedFillFieldWidth(
            fillWeight,
            totalFillWeight,
            nonFillWidth,
            viewportAlongFieldAxis,
            MinFieldWidth,
            ContentScale);
    }

    private double GetFieldBaseWidth(int fieldIndex)
    {
        var field = Fields[fieldIndex];
        return FieldWidthService.ResolveFieldBaseWidth(
            field.Width,
            _fieldWidthOverrides.TryGetValue(field, out var o),
            o,
            MinFieldWidth,
            ContentScale);
    }

    private void SetFieldWidth(int fieldIndex, double screenPixelWidth)
    {
        if (fieldIndex < 0 || fieldIndex >= Fields.Count)
        {
            return;
        }

        var field = Fields[fieldIndex];
        _fieldWidthOverrides[field] = Math.Max(MinFieldWidth, screenPixelWidth / ContentScale);
        InvalidateFieldFillWidthCache();
        UpdateScrollBars();
    }

    /// <summary>Sets field width in logical units (same as <see cref="IGriddoFieldView.Width"/>), independent of <see cref="Grid.Griddo.ContentScale"/>.</summary>
    public void SetLogicalFieldWidth(int fieldIndex, double logicalWidth)
    {
        SetFieldWidth(fieldIndex, Math.Max(MinFieldWidth, logicalWidth) * ContentScale);
        InvalidateVisual();
    }

    /// <summary>
    /// Gets the current logical width (same unit as <see cref="IGriddoFieldView.Width"/>), including user drag overrides.
    /// </summary>
    public double GetLogicalFieldWidth(int fieldIndex)
    {
        if (fieldIndex < 0 || fieldIndex >= Fields.Count)
        {
            return MinFieldWidth;
        }

        var field = Fields[fieldIndex];
        var logical = _fieldWidthOverrides.TryGetValue(field, out var overrideWidth)
            ? overrideWidth
            : field.Width;
        return Math.Max(MinFieldWidth, logical);
    }

    /// <summary>Clears per-field width overrides so layout uses each field view’s nominal <see cref="IGriddoFieldView.Width"/>.</summary>
    public void ClearFieldWidthOverrides()
    {
        if (_fieldWidthOverrides.Count == 0 && _userFixedWidthFields.Count == 0)
        {
            return;
        }

        _fieldWidthOverrides.Clear();
        _userFixedWidthFields.Clear();
        InvalidateFieldFillWidthCache();
        UpdateScrollBars();
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Marks a column width as user-chosen so automatic auto-width skips it until explicit auto-size.</summary>
    public void MarkFieldWidthUserFixed(int fieldIndex)
    {
        if (fieldIndex >= 0 && fieldIndex < Fields.Count)
        {
            _userFixedWidthFields.Add(Fields[fieldIndex]);
        }
    }

    private void ClearFieldWidthUserFixed(int fieldIndex)
    {
        if (fieldIndex >= 0 && fieldIndex < Fields.Count)
        {
            _userFixedWidthFields.Remove(Fields[fieldIndex]);
        }
    }

    private bool IsFieldWidthUserFixed(int fieldIndex) =>
        fieldIndex >= 0
        && fieldIndex < Fields.Count
        && _userFixedWidthFields.Contains(Fields[fieldIndex]);

    /// <summary>Clears <see cref="MarkInitialAutoWidthSuppressedForGridField"/> markers (e.g. before rebuilding fields).</summary>
    public void ClearInitialAutoWidthSuppressions() => _suppressInitialAutoWidthFields.Clear();

    /// <summary>Skip initial sample-based auto-width for this grid field index (e.g. width from persisted layout).</summary>
    public void MarkInitialAutoWidthSuppressedForGridField(int fieldIndex)
    {
        if (fieldIndex >= 0 && fieldIndex < Fields.Count)
        {
            _suppressInitialAutoWidthFields.Add(Fields[fieldIndex]);
        }
    }
    private void AutoSizeField(int fieldIndex)
    {
        if (fieldIndex < 0 || fieldIndex >= Fields.Count)
        {
            return;
        }

        ClearFieldWidthUserFixed(fieldIndex);
        var sampledRecords = GetAutoSizeSampleRecords(fieldIndex);
        var max = MeasureAutoWidthForField(fieldIndex, sampledRecords);
        SetFieldWidth(fieldIndex, max);
        _hasAutoSizedFields = true;
        InvalidateVisual();
        FieldWidthsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AutoSizeAllFields()
    {
        if (Fields.Count == 0)
        {
            return;
        }

        for (var fieldIndex = 0; fieldIndex < Fields.Count; fieldIndex++)
        {
            ClearFieldWidthUserFixed(fieldIndex);
            var sampledRecords = GetAutoSizeSampleRecords(fieldIndex);
            var max = MeasureAutoWidthForField(fieldIndex, sampledRecords);
            SetFieldWidth(fieldIndex, max);
        }

        _hasAutoSizedFields = true;
        InvalidateVisual();
    }

    /// <summary>
    /// Auto-sizes only columns that have not been manually resized (divider drag) or restored from persisted layout.
    /// </summary>
    public void AutoSizeNonUserFixedFields()
    {
        if (Fields.Count == 0)
        {
            return;
        }

        var any = false;
        for (var fieldIndex = 0; fieldIndex < Fields.Count; fieldIndex++)
        {
            if (IsFieldWidthUserFixed(fieldIndex))
            {
                continue;
            }

            var sampledRecords = GetAutoSizeSampleRecords(fieldIndex);
            var max = MeasureAutoWidthForField(fieldIndex, sampledRecords);
            SetFieldWidth(fieldIndex, max);
            any = true;
        }

        if (!any)
        {
            return;
        }

        _hasAutoSizedFields = true;
        InvalidateVisual();
    }

    public void AutoSizeFields(IEnumerable<int> fieldIndices)
    {
        if (Fields.Count == 0)
        {
            return;
        }

        var any = false;
        foreach (var idx in fieldIndices.Distinct())
        {
            if (idx < 0 || idx >= Fields.Count)
            {
                continue;
            }

            ClearFieldWidthUserFixed(idx);
            var sampledRecords = GetAutoSizeSampleRecords(idx);
            var max = MeasureAutoWidthForField(idx, sampledRecords);
            SetFieldWidth(idx, max);
            any = true;
        }

        if (!any)
        {
            return;
        }

        _hasAutoSizedFields = true;
        InvalidateVisual();
        FieldWidthsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AutoSizeFieldsFromSampleRecords()
    {
        if (Fields.Count == 0 || Records.Count == 0)
        {
            return;
        }

        for (var fieldIndex = 0; fieldIndex < Fields.Count; fieldIndex++)
        {
            if (ShouldSkipInitialSampleAutoWidth(fieldIndex))
            {
                continue;
            }

            var sampledRecords = GetAutoSizeSampleRecords(fieldIndex);
            var max = MeasureAutoWidthForField(fieldIndex, sampledRecords);
            SetFieldWidth(fieldIndex, max);
        }

        _hasAutoSizedFields = true;
        InvalidateVisual();
    }

    /// <summary>
    /// Initial deferred auto-width (when records first appear) runs only for columns that do not already have a width:
    /// persisted/user override, layout-applier suppression, or a host-declared <see cref="IGriddoFieldView.Width"/> &gt; 0.
    /// Use <c>Width == 0</c> on the field view to request sample-based width when data loads.
    /// </summary>
    private bool ShouldSkipInitialSampleAutoWidth(int fieldIndex)
    {
        if (fieldIndex < 0 || fieldIndex >= Fields.Count)
        {
            return true;
        }

        var field = Fields[fieldIndex];
        if (_userFixedWidthFields.Contains(field))
        {
            return true;
        }

        if (_suppressInitialAutoWidthFields.Contains(field))
        {
            return true;
        }

        if (_fieldWidthOverrides.ContainsKey(field))
        {
            return true;
        }

        if (field.Width > 0)
        {
            return true;
        }

        return false;
    }

    private void ScheduleInitialSampleAutoSize()
    {
        if (_initialSampleAutoSizeScheduled)
        {
            return;
        }

        _initialSampleAutoSizeScheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _initialSampleAutoSizeScheduled = false;
            if (_hasAutoSizedFields || Records.Count == 0 || Fields.Count == 0)
            {
                return;
            }

            AutoSizeFieldsFromSampleRecords();
        }));
    }

    private const int MaxTextAutoSizeSampleCount = 10;

    private List<int> GetAutoSizeSampleRecords(int fieldIndex)
    {
        if (Records.Count == 0 || fieldIndex < 0 || fieldIndex >= Fields.Count)
        {
            return [];
        }

        var field = Fields[fieldIndex];
        var longestTextRecords = new List<(int RecordIndex, int TextLength)>();
        var nonTextRecordIndices = new List<int>();
        for (var recordIndex = 0; recordIndex < Records.Count; recordIndex++)
        {
            var sampleText = GetAutoSizeSampleText(field, Records[recordIndex]);
            if (sampleText is not null)
            {
                longestTextRecords.Add((recordIndex, sampleText.Length));
                continue;
            }

            if (field is IGriddoHostedFieldView)
            {
                continue;
            }

            var raw = field.GetValue(Records[recordIndex]);
            if (raw is ImageSource or Geometry)
            {
                nonTextRecordIndices.Add(recordIndex);
            }
        }

        if (longestTextRecords.Count > 0)
        {
            return longestTextRecords
                .OrderByDescending(x => x.TextLength)
                .ThenBy(x => x.RecordIndex)
                .Take(MaxTextAutoSizeSampleCount)
                .Select(x => x.RecordIndex)
                .OrderBy(x => x)
                .ToList();
        }

        if (nonTextRecordIndices.Count > 0)
        {
            return BuildAutoSizeSampleRecordIndices(nonTextRecordIndices);
        }

        return BuildAutoSizeSampleRecordIndices(Enumerable.Range(0, Records.Count));
    }

    private static List<int> BuildAutoSizeSampleRecordIndices(IEnumerable<int> candidateRecordIndices)
    {
        var candidates = candidateRecordIndices.Distinct().OrderBy(x => x).ToList();
        if (candidates.Count == 0)
        {
            return [];
        }

        if (candidates.Count == 1)
        {
            return candidates;
        }

        var sampledRecords = new HashSet<int> { candidates[0], candidates[^1] };
        var randomTargetCount = Math.Min(10, Math.Max(0, candidates.Count - sampledRecords.Count));
        while (sampledRecords.Count < randomTargetCount + 2 && sampledRecords.Count < candidates.Count)
        {
            sampledRecords.Add(candidates[Random.Shared.Next(0, candidates.Count)]);
        }

        return sampledRecords.OrderBy(x => x).ToList();
    }

    private static string? GetAutoSizeSampleText(IGriddoFieldView field, object record)
    {
        if (field is IGriddoHostedFieldView)
        {
            return null;
        }

        var raw = field.GetValue(record);
        if (raw is ImageSource or Geometry)
        {
            return null;
        }

        if (field.IsHtml)
        {
            var html = raw?.ToString();
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            var text = ExtractHtmlPreviewText(html);
            return text.Length == 0 ? null : text;
        }

        var formatted = field.FormatValue(raw);
        return string.IsNullOrEmpty(formatted) ? null : formatted;
    }

    private double MeasureAutoWidthForField(int fieldIndex, IReadOnlyCollection<int> sampledRecords)
    {
        var field = Fields[fieldIndex];
        const double HtmlAutoWidthMaxDip = 420d;
        const double HtmlAutoHeightMaxDip = 220d;
        if (field is IGriddoHostedFieldView)
        {
            return Math.Max(MinFieldWidth * ContentScale, GetFieldWidth(fieldIndex));
        }

        var fallbackTypeface = new Typeface("Segoe UI");
        var typeface = ResolveFieldTypeface(fieldIndex, fallbackTypeface, null);
        var fontSize = ResolveFieldFontSize(fieldIndex, null);
        var pad = 12 * _contentScale;
        if (IsBodyTransposed)
        {
            // Field bands stack vertically: size from header + cell content height (not line width).
            var maxH = MeasureTextHeight(field.Header, typeface, fontSize) + pad;
            foreach (var recordIndex in sampledRecords)
            {
                if (recordIndex < 0 || recordIndex >= Records.Count)
                {
                    continue;
                }

                var raw = field.GetValue(Records[recordIndex]);
                if (raw is ImageSource or Geometry)
                {
                    maxH = Math.Max(maxH, MeasureCellHeight(raw, typeface, fontSize) + pad);
                    continue;
                }

                if (field.IsHtml)
                {
                    var renderedH = GriddoValuePainter.MeasureRenderedHeight(raw, typeface, fontSize, treatAsHtml: true);
                    if (renderedH > 0)
                    {
                        maxH = Math.Max(maxH, Math.Min(renderedH, HtmlAutoHeightMaxDip * ContentScale) + pad);
                    }

                    continue;
                }

                var text = field.FormatValue(raw);
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                maxH = Math.Max(maxH, MeasureTextHeight(text, typeface, fontSize) + pad);
            }

            return maxH;
        }

        var max = MeasureTextWidth(field.Header, typeface, fontSize) + pad;
        foreach (var recordIndex in sampledRecords)
        {
            if (recordIndex < 0 || recordIndex >= Records.Count)
            {
                continue;
            }

            var raw = field.GetValue(Records[recordIndex]);
            if (raw is ImageSource or Geometry)
            {
                continue;
            }

            if (field.IsHtml)
            {
                var previewWidth = MeasureHtmlAutoWidth(raw, typeface, fontSize);
                if (previewWidth > 0)
                {
                    // Use compact plain-text preview width for HTML fields.
                    max = Math.Max(max, Math.Min(previewWidth, HtmlAutoWidthMaxDip * ContentScale) + pad);
                }

                continue;
            }

            var text = field.FormatValue(raw);
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            max = Math.Max(max, MeasureTextWidth(text, typeface, fontSize) + pad);
        }

        return max;
    }

    private static double MeasureHtmlAutoWidth(object? raw, Typeface typeface, double fontSize)
    {
        var html = raw?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(html))
        {
            return 0;
        }

        var text = ExtractHtmlPreviewText(html);
        if (text.Length == 0)
        {
            return 0;
        }

        const int maxCharsPerLine = 42;
        var max = 0d;
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var compact = string.Join(' ', line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            if (compact.Length == 0)
            {
                continue;
            }

            var sample = compact.Length > maxCharsPerLine
                ? compact[..maxCharsPerLine] + "..."
                : compact;
            max = Math.Max(max, MeasureTextWidth(sample, typeface, fontSize));
        }

        return max;
    }

    private static string ExtractHtmlPreviewText(string html)
    {
        var withBreaks = html
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</tr>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</div>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</li>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</td>", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("</th>", " ", StringComparison.OrdinalIgnoreCase);

        var chars = new List<char>(withBreaks.Length);
        var insideTag = false;
        foreach (var ch in withBreaks)
        {
            if (ch == '<')
            {
                insideTag = true;
                continue;
            }

            if (ch == '>')
            {
                insideTag = false;
                continue;
            }

            if (!insideTag)
            {
                chars.Add(ch);
            }
        }

        var plain = new string(chars.ToArray());
        return plain
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("&lt;", "<", StringComparison.OrdinalIgnoreCase)
            .Replace("&gt;", ">", StringComparison.OrdinalIgnoreCase)
            .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase)
            .Replace("&quot;", "\"", StringComparison.OrdinalIgnoreCase)
            .Replace("&#39;", "'", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }
}
