using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Threading;

namespace Griddo;

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

        var remaining = Math.Max(0, _viewportBodyWidth - nonFillWidth);
        var perFill = fillCount > 0 ? remaining / fillCount : 0;
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
    private void AutoSizeColumn(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= Columns.Count)
        {
            return;
        }

        var typeface = new Typeface("Segoe UI");
        var pad = 12 * _contentScale;
        var max = MeasureTextWidth(Columns[columnIndex].Header, typeface, EffectiveFontSize) + pad;
        for (var row = 0; row < Rows.Count; row++)
        {
            var column = Columns[columnIndex];
            if (column is IGriddoHostedColumnView)
            {
                continue;
            }

            var value = column.GetValue(Rows[row]);
            if (value is ImageSource or Geometry)
            {
                continue;
            }

            if (column.IsHtml)
            {
                // Measure HTML using rendered run widths without editor/body wrapping constraints.
                var renderedWidth = GriddoValuePainter.MeasureRenderedWidth(value, typeface, EffectiveFontSize, treatAsHtml: true);
                if (renderedWidth > 0)
                {
                    max = Math.Max(max, renderedWidth + pad);
                }

                continue;
            }

            var text = column.FormatValue(value);
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            max = Math.Max(max, MeasureTextWidth(text, typeface, EffectiveFontSize) + pad);
        }

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

        for (var columnIndex = 0; columnIndex < Columns.Count; columnIndex++)
        {
            AutoSizeColumn(columnIndex);
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

        var sampledRows = new HashSet<int> { 0, Rows.Count - 1 };
        var randomTargetCount = Math.Min(10, Math.Max(0, Rows.Count - sampledRows.Count));
        while (sampledRows.Count < randomTargetCount + 2 && sampledRows.Count < Rows.Count)
        {
            sampledRows.Add(Random.Shared.Next(0, Rows.Count));
        }

        var typeface = new Typeface("Segoe UI");
        var pad = 12 * _contentScale;
        for (var columnIndex = 0; columnIndex < Columns.Count; columnIndex++)
        {
            var column = Columns[columnIndex];
            if (column is IGriddoHostedColumnView)
            {
                continue;
            }

            var max = MeasureTextWidth(column.Header, typeface, EffectiveFontSize) + pad;
            foreach (var rowIndex in sampledRows)
            {
                if (rowIndex < 0 || rowIndex >= Rows.Count)
                {
                    continue;
                }

                var value = column.GetValue(Rows[rowIndex]);
                if (value is ImageSource or Geometry)
                {
                    continue;
                }

                if (column.IsHtml)
                {
                    var renderedWidth = GriddoValuePainter.MeasureRenderedWidth(value, typeface, EffectiveFontSize, treatAsHtml: true);
                    if (renderedWidth > 0)
                    {
                        max = Math.Max(max, renderedWidth + pad);
                    }

                    continue;
                }

                var text = column.FormatValue(value);
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                max = Math.Max(max, MeasureTextWidth(text, typeface, EffectiveFontSize) + pad);
            }

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
            if (_hasAutoSizedColumns || _columnWidthOverrides.Count > 0 || Rows.Count == 0 || Columns.Count == 0)
            {
                return;
            }

            AutoSizeColumnsFromSampleRows();
        }));
    }
}
