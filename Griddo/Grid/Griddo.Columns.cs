using System.Windows.Media;
using System.Windows.Threading;
using Griddo.Columns;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    private double GetColumnWidth(int columnIndex)
    {
        if (columnIndex >= 0 && columnIndex < Columns.Count && Columns[columnIndex].Fill)
        {
            return GetFillColumnWidth();
        }

        return GetColumnBaseWidth(columnIndex);
    }

    private double GetColumnBaseWidth(int columnIndex)
    {
        var logical = _columnWidthOverrides.TryGetValue(columnIndex, out var o)
            ? o
            : Columns[columnIndex].Width;
        return Math.Max(MinColumnWidth, logical) * ContentScale;
    }

    private double GetFillColumnWidth()
    {
        var fillCount = Columns.Count(c => c.Fill);
        if (fillCount <= 0)
        {
            return MinColumnWidth * ContentScale;
        }

        var nonFillWidth = 0.0;
        for (var i = 0; i < Columns.Count; i++)
        {
            if (!Columns[i].Fill)
            {
                nonFillWidth += GetColumnBaseWidth(i);
            }
        }

        var viewportAlongColumnAxis = IsBodyTransposed ? _viewportBodyHeight : _viewportBodyWidth;
        var remaining = Math.Max(0, viewportAlongColumnAxis - nonFillWidth);
        var perFill = remaining / fillCount;
        return Math.Max(MinColumnWidth * ContentScale, perFill);
    }

    private void SetColumnWidth(int columnIndex, double screenPixelWidth)
    {
        if (columnIndex < 0 || columnIndex >= Columns.Count)
        {
            return;
        }

        _columnWidthOverrides[columnIndex] = Math.Max(MinColumnWidth, screenPixelWidth / ContentScale);
        UpdateScrollBars();
    }

    /// <summary>Sets column width in logical units (same as <see cref="IGriddoColumnView.Width"/>), independent of <see cref="ContentScale"/>.</summary>
    public void SetLogicalColumnWidth(int columnIndex, double logicalWidth)
    {
        SetColumnWidth(columnIndex, Math.Max(MinColumnWidth, logicalWidth) * ContentScale);
        InvalidateVisual();
    }

    /// <summary>
    /// Gets the current logical width (same unit as <see cref="IGriddoColumnView.Width"/>), including user drag overrides.
    /// </summary>
    public double GetLogicalColumnWidth(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= Columns.Count)
        {
            return MinColumnWidth;
        }

        var logical = _columnWidthOverrides.TryGetValue(columnIndex, out var overrideWidth)
            ? overrideWidth
            : Columns[columnIndex].Width;
        return Math.Max(MinColumnWidth, logical);
    }

    /// <summary>Clears per-column width overrides so layout uses each column view’s nominal <see cref="IGriddoColumnView.Width"/>.</summary>
    public void ClearColumnWidthOverrides()
    {
        if (_columnWidthOverrides.Count == 0)
        {
            return;
        }

        _columnWidthOverrides.Clear();
        UpdateScrollBars();
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Clears <see cref="MarkInitialAutoWidthSuppressedForGridColumn"/> markers (e.g. before rebuilding columns).</summary>
    public void ClearInitialAutoWidthSuppressions() => _suppressInitialAutoWidthColumns.Clear();

    /// <summary>Skip initial sample-based auto-width for this grid column index (e.g. width from persisted layout).</summary>
    public void MarkInitialAutoWidthSuppressedForGridColumn(int columnIndex)
    {
        if (columnIndex >= 0 && columnIndex < Columns.Count)
        {
            _suppressInitialAutoWidthColumns.Add(columnIndex);
        }
    }
    private void AutoSizeColumn(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= Columns.Count)
        {
            return;
        }
        var sampledRows = GetAutoSizeSampleRows();
        var max = MeasureAutoWidthForColumn(columnIndex, sampledRows);
        SetColumnWidth(columnIndex, max);
        _hasAutoSizedColumns = true;
        InvalidateVisual();
    }

    public void AutoSizeAllColumns()
    {
        if (Columns.Count == 0)
        {
            return;
        }

        var sampledRows = GetAutoSizeSampleRows();
        for (var columnIndex = 0; columnIndex < Columns.Count; columnIndex++)
        {
            var max = MeasureAutoWidthForColumn(columnIndex, sampledRows);
            SetColumnWidth(columnIndex, max);
        }

        _hasAutoSizedColumns = true;
        InvalidateVisual();
    }

    public void AutoSizeColumns(IEnumerable<int> columnIndices)
    {
        if (Columns.Count == 0)
        {
            return;
        }

        var any = false;
        var sampledRows = GetAutoSizeSampleRows();
        foreach (var idx in columnIndices.Distinct())
        {
            if (idx < 0 || idx >= Columns.Count)
            {
                continue;
            }

            var max = MeasureAutoWidthForColumn(idx, sampledRows);
            SetColumnWidth(idx, max);
            any = true;
        }

        if (!any)
        {
            return;
        }

        _hasAutoSizedColumns = true;
        InvalidateVisual();
    }

    private void AutoSizeColumnsFromSampleRows()
    {
        if (Columns.Count == 0 || Rows.Count == 0)
        {
            return;
        }

        var sampledRows = GetAutoSizeSampleRows();
        for (var columnIndex = 0; columnIndex < Columns.Count; columnIndex++)
        {
            if (_suppressInitialAutoWidthColumns.Contains(columnIndex))
            {
                continue;
            }

            var max = MeasureAutoWidthForColumn(columnIndex, sampledRows);
            SetColumnWidth(columnIndex, max);
        }

        _hasAutoSizedColumns = true;
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
            if (_hasAutoSizedColumns || Rows.Count == 0 || Columns.Count == 0)
            {
                return;
            }

            AutoSizeColumnsFromSampleRows();
        }));
    }

    private List<int> GetAutoSizeSampleRows()
    {
        if (Rows.Count == 0)
        {
            return [];
        }

        var sampledRows = new HashSet<int> { 0, Rows.Count - 1 };
        var randomTargetCount = Math.Min(10, Math.Max(0, Rows.Count - sampledRows.Count));
        while (sampledRows.Count < randomTargetCount + 2 && sampledRows.Count < Rows.Count)
        {
            sampledRows.Add(Random.Shared.Next(0, Rows.Count));
        }

        return sampledRows.OrderBy(x => x).ToList();
    }

    private double MeasureAutoWidthForColumn(int columnIndex, IReadOnlyCollection<int> sampledRows)
    {
        var column = Columns[columnIndex];
        if (column is IGriddoHostedColumnView)
        {
            return Math.Max(MinColumnWidth * ContentScale, GetColumnWidth(columnIndex));
        }

        var fallbackTypeface = new Typeface("Segoe UI");
        var typeface = ResolveColumnTypeface(columnIndex, fallbackTypeface, null);
        var fontSize = ResolveColumnFontSize(columnIndex, null);
        var pad = 12 * _contentScale;
        if (IsBodyTransposed)
        {
            // Column bands stack vertically: size from header + cell content height (not line width).
            var maxH = MeasureTextHeight(column.Header, typeface, fontSize) + pad;
            foreach (var rowIndex in sampledRows)
            {
                if (rowIndex < 0 || rowIndex >= Rows.Count)
                {
                    continue;
                }

                var raw = column.GetValue(Rows[rowIndex]);
                if (raw is ImageSource or Geometry)
                {
                    maxH = Math.Max(maxH, MeasureCellHeight(raw, typeface, fontSize) + pad);
                    continue;
                }

                if (column.IsHtml)
                {
                    var renderedH = GriddoValuePainter.MeasureRenderedHeight(raw, typeface, fontSize, treatAsHtml: true);
                    if (renderedH > 0)
                    {
                        maxH = Math.Max(maxH, renderedH + pad);
                    }

                    continue;
                }

                var text = column.FormatValue(raw);
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                maxH = Math.Max(maxH, MeasureTextHeight(text, typeface, fontSize) + pad);
            }

            return maxH;
        }

        var max = MeasureTextWidth(column.Header, typeface, fontSize) + pad;
        foreach (var rowIndex in sampledRows)
        {
            if (rowIndex < 0 || rowIndex >= Rows.Count)
            {
                continue;
            }

            var raw = column.GetValue(Rows[rowIndex]);
            if (raw is ImageSource or Geometry)
            {
                continue;
            }

            if (column.IsHtml)
            {
                var renderedWidth = GriddoValuePainter.MeasureRenderedWidth(raw, typeface, fontSize, treatAsHtml: true);
                if (renderedWidth > 0)
                {
                    max = Math.Max(max, renderedWidth + pad);
                }

                continue;
            }

            var text = column.FormatValue(raw);
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            max = Math.Max(max, MeasureTextWidth(text, typeface, fontSize) + pad);
        }

        return max;
    }
}
