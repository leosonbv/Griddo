using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Griddo.Fields;
using Griddo.Primitives;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    private const double HtmlCellScrollBarWidth = 10.0;
    private readonly Dictionary<GriddoCellAddress, double> _htmlCellVerticalScrollOffsets = new();

    private bool FieldUsesHtmlCellScroll(int fieldIndex) =>
        fieldIndex >= 0
        && fieldIndex < Fields.Count
        && Fields[fieldIndex].IsHtml
        && Fields[fieldIndex] is IGriddoFieldHtmlScrollView { AutoVerticalScrollBar: true };

    private double GetHtmlCellScrollOffset(GriddoCellAddress cell) =>
        _htmlCellVerticalScrollOffsets.TryGetValue(cell, out var offset) ? offset : 0;

    private void SetHtmlCellScrollOffset(GriddoCellAddress cell, double offset, double maxScroll)
    {
        if (!cell.IsValid)
        {
            return;
        }

        var clamped = Math.Clamp(offset, 0, Math.Max(0, maxScroll));
        if (clamped <= 1e-6)
        {
            _htmlCellVerticalScrollOffsets.Remove(cell);
        }
        else
        {
            _htmlCellVerticalScrollOffsets[cell] = clamped;
        }

        InvalidateVisual();
    }

    private int GetFirstVisibleScrollRecordIndex(double verticalOffset)
    {
        if (Records.Count == 0 || _viewportBodyHeight <= 0 || IsBodyTransposed)
        {
            return -1;
        }

        var recordHeight = GetRecordHeight(0);
        if (recordHeight <= 1e-6)
        {
            return -1;
        }

        var fixedRecords = GetEffectiveFixedRecordCount();
        var scrollTop = fixedRecords * recordHeight;
        var scrollViewportHeight = _viewportBodyHeight - scrollTop;
        if (scrollViewportHeight <= 0 || fixedRecords >= Records.Count)
        {
            return Math.Clamp(fixedRecords, 0, Records.Count - 1);
        }

        var first = fixedRecords + (int)Math.Floor(verticalOffset / recordHeight);
        return Math.Clamp(first, 0, Records.Count - 1);
    }

    private void ResetHtmlCellScrollForRecord(int recordIndex)
    {
        if (recordIndex < 0)
        {
            return;
        }

        List<GriddoCellAddress>? toRemove = null;
        foreach (var cell in _htmlCellVerticalScrollOffsets.Keys)
        {
            if (cell.RecordIndex != recordIndex)
            {
                continue;
            }

            toRemove ??= [];
            toRemove.Add(cell);
        }

        if (toRemove is null)
        {
            return;
        }

        foreach (var cell in toRemove)
        {
            _htmlCellVerticalScrollOffsets.Remove(cell);
        }
    }

    private void ResetHtmlCellScrollAfterVerticalScroll(double oldOffset, double newOffset)
    {
        if (Records.Count == 0 || IsBodyTransposed || Math.Abs(oldOffset - newOffset) <= 1e-6)
        {
            return;
        }

        var oldFirst = GetFirstVisibleScrollRecordIndex(oldOffset);
        var newFirst = GetFirstVisibleScrollRecordIndex(newOffset);
        if (newFirst < 0 || newFirst == oldFirst)
        {
            return;
        }

        // When the table scrolls to another record, show its HTML from the top.
        ResetHtmlCellScrollForRecord(newFirst);
    }

    private bool TryGetHtmlCellScrollMetrics(
        GriddoCellAddress cell,
        Rect paintBounds,
        object? rawValue,
        Typeface typeface,
        double fontSize,
        bool noWrap,
        out double scrollOffset,
        out double contentHeight,
        out double viewportHeight,
        out double maxScroll,
        out Rect contentBounds,
        out Rect scrollBarBounds)
    {
        scrollOffset = 0;
        contentHeight = 0;
        viewportHeight = 0;
        maxScroll = 0;
        contentBounds = paintBounds;
        scrollBarBounds = Rect.Empty;

        if (!cell.IsValid || !FieldUsesHtmlCellScroll(cell.FieldIndex) || paintBounds.IsEmpty)
        {
            return false;
        }

        var html = rawValue?.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(html))
        {
            return false;
        }

        const double padX = 4.0;
        const double padY = 2.0;
        scrollBarBounds = new Rect(
            paintBounds.Right - HtmlCellScrollBarWidth,
            paintBounds.Y,
            HtmlCellScrollBarWidth,
            paintBounds.Height);
        contentBounds = new Rect(
            paintBounds.X,
            paintBounds.Y,
            Math.Max(1, paintBounds.Width - HtmlCellScrollBarWidth),
            paintBounds.Height);

        viewportHeight = Math.Max(1, contentBounds.Height - padY * 2);
        contentHeight = GriddoValuePainter.MeasureHtmlRenderedHeight(
            html,
            typeface,
            fontSize,
            Math.Max(1, contentBounds.Width - padX * 2),
            noWrap);

        if (contentHeight <= viewportHeight + 1)
        {
            contentBounds = paintBounds;
            scrollBarBounds = Rect.Empty;
            return false;
        }

        maxScroll = Math.Max(0, contentHeight - viewportHeight);
        scrollOffset = Math.Clamp(GetHtmlCellScrollOffset(cell), 0, maxScroll);
        return true;
    }

    private bool TryHandleHtmlCellMouseWheel(Point pointer, MouseWheelEventArgs e)
    {
        var cell = HitTestCell(pointer);
        if (!cell.IsValid || !FieldUsesHtmlCellScroll(cell.FieldIndex))
        {
            return false;
        }

        if (!TryGetCellPaintBounds(cell, out var paintBounds))
        {
            return false;
        }

        var field = Fields[cell.FieldIndex];
        var rawValue = field.GetValue(Records[cell.RecordIndex]);
        var noWrap = field is IGriddoFieldWrapView { NoWrap: true };
        if (!TryGetHtmlCellScrollMetrics(
                cell,
                paintBounds,
                rawValue,
                ResolveFieldTypeface(cell.FieldIndex, new Typeface("Segoe UI"), ResolveCellPropertyView(Records[cell.RecordIndex], cell.FieldIndex)),
                ResolveFieldFontSize(cell.FieldIndex, ResolveCellPropertyView(Records[cell.RecordIndex], cell.FieldIndex)),
                noWrap,
                out var scrollOffset,
                out var contentHeight,
                out var viewportHeight,
                out var maxScroll,
                out _,
                out _))
        {
            return false;
        }

        var step = Math.Max(16, viewportHeight * 0.12);
        var scrollingUp = e.Delta > 0;
        if (scrollingUp && scrollOffset <= 1e-6)
        {
            return false;
        }

        if (!scrollingUp && scrollOffset >= maxScroll - 1e-6)
        {
            return false;
        }

        var next = scrollOffset + (scrollingUp ? -step : step);
        SetHtmlCellScrollOffset(cell, next, maxScroll);
        e.Handled = true;
        return true;
    }

    private bool TryGetCellPaintBounds(GriddoCellAddress cell, out Rect paintBounds)
    {
        paintBounds = Rect.Empty;
        if (!cell.IsValid || cell.RecordIndex < 0 || cell.RecordIndex >= Records.Count)
        {
            return false;
        }

        paintBounds = GetCellRect(cell.RecordIndex, cell.FieldIndex);
        return !paintBounds.IsEmpty;
    }
}
