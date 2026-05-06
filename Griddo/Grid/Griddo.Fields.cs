using System.Windows.Media;
using System.Windows.Threading;
using Griddo.Fields;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    private double GetFieldWidth(int fieldIndex)
    {
        if (fieldIndex >= 0 && fieldIndex < Fields.Count && Fields[fieldIndex].Fill)
        {
            return GetFillFieldWidth();
        }

        return GetFieldBaseWidth(fieldIndex);
    }

    private double GetFieldBaseWidth(int fieldIndex)
    {
        var field = Fields[fieldIndex];
        var logical = _fieldWidthOverrides.TryGetValue(field, out var o)
            ? o
            : field.Width;
        return Math.Max(MinFieldWidth, logical) * ContentScale;
    }

    private double GetFillFieldWidth()
    {
        var fillCount = Fields.Count(c => c.Fill);
        if (fillCount <= 0)
        {
            return MinFieldWidth * ContentScale;
        }

        var nonFillWidth = 0.0;
        for (var i = 0; i < Fields.Count; i++)
        {
            if (!Fields[i].Fill)
            {
                nonFillWidth += GetFieldBaseWidth(i);
            }
        }

        var viewportAlongFieldAxis = IsBodyTransposed ? _viewportBodyHeight : _viewportBodyWidth;
        var remaining = Math.Max(0, viewportAlongFieldAxis - nonFillWidth);
        var perFill = remaining / fillCount;
        return Math.Max(MinFieldWidth * ContentScale, perFill);
    }

    private void SetFieldWidth(int fieldIndex, double screenPixelWidth)
    {
        if (fieldIndex < 0 || fieldIndex >= Fields.Count)
        {
            return;
        }

        var field = Fields[fieldIndex];
        _fieldWidthOverrides[field] = Math.Max(MinFieldWidth, screenPixelWidth / ContentScale);
        UpdateScrollBars();
    }

    /// <summary>Sets field width in logical units (same as <see cref="IGriddoFieldView.Width"/>), independent of <see cref="ContentScale"/>.</summary>
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
        if (_fieldWidthOverrides.Count == 0)
        {
            return;
        }

        _fieldWidthOverrides.Clear();
        UpdateScrollBars();
        InvalidateMeasure();
        InvalidateVisual();
    }

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
        var sampledRecords = GetAutoSizeSampleRecords();
        var max = MeasureAutoWidthForField(fieldIndex, sampledRecords);
        SetFieldWidth(fieldIndex, max);
        _hasAutoSizedFields = true;
        InvalidateVisual();
    }

    public void AutoSizeAllFields()
    {
        if (Fields.Count == 0)
        {
            return;
        }

        var sampledRecords = GetAutoSizeSampleRecords();
        for (var fieldIndex = 0; fieldIndex < Fields.Count; fieldIndex++)
        {
            var max = MeasureAutoWidthForField(fieldIndex, sampledRecords);
            SetFieldWidth(fieldIndex, max);
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
        var sampledRecords = GetAutoSizeSampleRecords();
        foreach (var idx in fieldIndices.Distinct())
        {
            if (idx < 0 || idx >= Fields.Count)
            {
                continue;
            }

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
    }

    private void AutoSizeFieldsFromSampleRecords()
    {
        if (Fields.Count == 0 || Records.Count == 0)
        {
            return;
        }

        var sampledRecords = GetAutoSizeSampleRecords();
        for (var fieldIndex = 0; fieldIndex < Fields.Count; fieldIndex++)
        {
            if (_suppressInitialAutoWidthFields.Contains(Fields[fieldIndex]))
            {
                continue;
            }

            var max = MeasureAutoWidthForField(fieldIndex, sampledRecords);
            SetFieldWidth(fieldIndex, max);
        }

        _hasAutoSizedFields = true;
        InvalidateVisual();
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

    private List<int> GetAutoSizeSampleRecords()
    {
        if (Records.Count == 0)
        {
            return [];
        }

        var sampledRecords = new HashSet<int> { 0, Records.Count - 1 };
        var randomTargetCount = Math.Min(10, Math.Max(0, Records.Count - sampledRecords.Count));
        while (sampledRecords.Count < randomTargetCount + 2 && sampledRecords.Count < Records.Count)
        {
            sampledRecords.Add(Random.Shared.Next(0, Records.Count));
        }

        return sampledRecords.OrderBy(x => x).ToList();
    }

    private double MeasureAutoWidthForField(int fieldIndex, IReadOnlyCollection<int> sampledRecords)
    {
        var field = Fields[fieldIndex];
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
                        maxH = Math.Max(maxH, renderedH + pad);
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
                var renderedWidth = GriddoValuePainter.MeasureRenderedWidth(raw, typeface, fontSize, treatAsHtml: true);
                if (renderedWidth > 0)
                {
                    max = Math.Max(max, renderedWidth + pad);
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
}
