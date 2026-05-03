using System.Windows;
using System.Windows.Media;
using Griddo.Columns;
using Griddo.Primitives;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    protected override void OnRender(DrawingContext dc)
    {
        SyncHostedCells();
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));
        DrawHeaders(dc);
        DrawBody(dc);
        DrawEditingText(dc);
        DrawCurrentCellOverlay(dc);
        DrawRowMoveCue(dc);
        DrawScrollBarCorner(dc);
    }

    private void DrawColumnHeader(DrawingContext dc, int col, double x, Typeface typeface)
    {
        var width = GetColumnWidth(col);
        var rect = new Rect(x, 0, width, ScaledColumnHeaderHeight);
        var headerBackground = (IsColumnHeaderMarkedSelected(col) && !HideHeaderSelectionColoring) ? SelectionBackground : HeaderBackground;
        dc.DrawRectangle(headerBackground, null, rect);
        var pen = new Pen(GridLineBrush, GridPenThickness);
        // Top edge of header strip is drawn once in DrawOuterWorksheetFrame (matches DrawLine rasterization for scroll columns).
        dc.DrawLine(pen, rect.TopRight, rect.BottomRight);
        if (col == 0)
        {
            dc.DrawLine(pen, rect.TopLeft, rect.BottomLeft);
        }
        var headerText = new FormattedText(
            Columns[col].Header,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            EffectiveFontSize,
            Brushes.Black,
            1.0);
        headerText.SetFontWeight(FontWeights.Bold);
        headerText.MaxTextWidth = Math.Max(1, rect.Width - 8);
        headerText.MaxTextHeight = Math.Max(1, rect.Height - 4);
        headerText.Trimming = TextTrimming.CharacterEllipsis;
        var headerY = rect.Y + Math.Max(0, (rect.Height - headerText.Height) / 2);
        dc.DrawText(headerText, new Point(rect.X + 4, headerY));

        if (_fixedColumnCount > 0 && col == _fixedColumnCount - 1)
        {
            dc.DrawLine(
                new Pen(FixedColumnRightBorderBrush, GridPenThickness),
                new Point(rect.Right, rect.Top),
                new Point(rect.Right, rect.Bottom));
        }
    }

    private void DrawBodyCell(
        DrawingContext dc,
        int row,
        int col,
        double x,
        double y,
        double rowHeight,
        object rowData,
        Rect bodyViewport,
        Typeface typeface)
    {
        var colWidth = GetColumnWidth(col);
        var rect = new Rect(x, y, colWidth, rowHeight);
        var address = new GriddoCellAddress(row, col);

        var isHostedCellEditing = IsHostedCellInEditMode(address);
        if (_findMatchedCells.Contains(address))
        {
            dc.DrawRectangle(FindMatchBackground, null, rect);
        }

        if (_selectedCells.Contains(address) && !isHostedCellEditing && !HideCellSelectionColoring)
        {
            dc.DrawRectangle(SelectionBackground, null, rect);
        }

        dc.DrawRectangle(null, new Pen(GridLineBrush, GridPenThickness), rect);
        if (_fixedColumnCount > 0 && col == _fixedColumnCount - 1)
        {
            dc.DrawLine(
                new Pen(FixedColumnRightBorderBrush, GridPenThickness),
                new Point(rect.Right, rect.Top),
                new Point(rect.Right, rect.Bottom));
        }

        if (Columns[col] is IGriddoHostedColumnView)
        {
            return;
        }

        if (Columns[col] is GriddoBoolColumnView)
        {
            var rawBool = Columns[col].GetValue(rowData);
            var isChecked = rawBool is true;
            var boolPaintBounds = Rect.Intersect(rect, bodyViewport);
            if (!boolPaintBounds.IsEmpty)
            {
                GriddoValuePainter.DrawBoolCheckbox(dc, isChecked, boolPaintBounds, EffectiveFontSize);
            }

            return;
        }

        var value = Columns[col].GetValue(rowData);
        var isGraphic = value is ImageSource or Geometry;
        // Intersect with viewport so HTML (and plain text) centers in the visible strip when the row is clipped vertically.
        var paintBounds = isGraphic ? rect : Rect.Intersect(rect, bodyViewport);
        if (!paintBounds.IsEmpty)
        {
            GriddoValuePainter.Paint(
                dc,
                value,
                paintBounds,
                typeface,
                EffectiveFontSize,
                Brushes.Black,
                Columns[col].IsHtml,
                true,
                Columns[col].ContentAlignment,
                isGraphic ? VerticalAlignment.Top : VerticalAlignment.Center);
        }
    }

    private void DrawHeaders(DrawingContext dc)
    {
        // Fixed top-left corner header cell (row-header column title area). Outer top/left strokes live in DrawOuterWorksheetFrame only.
        var cornerRect = new Rect(0, 0, _rowHeaderWidth, ScaledColumnHeaderHeight);
        dc.DrawRectangle(HeaderBackground, null, cornerRect);

        var typeface = new Typeface("Segoe UI");
        var fixedW = GetFixedColumnsWidth();
        var scrollLeft = _rowHeaderWidth + fixedW;

        if (_fixedColumnCount < Columns.Count && scrollLeft < _rowHeaderWidth + _viewportBodyWidth)
        {
            var scrollClipW = Math.Max(0, _viewportBodyWidth - fixedW);
            var scrollClip = new Rect(scrollLeft, 0, scrollClipW, ScaledColumnHeaderHeight);
            dc.PushClip(new RectangleGeometry(scrollClip));
            GetVisibleScrollColumnRange(out var sCol, out var eCol, out var x);
            if (eCol >= sCol)
            {
                for (var col = sCol; col <= eCol; col++)
                {
                    DrawColumnHeader(dc, col, x, typeface);
                    x += GetColumnWidth(col);
                }
            }

            dc.Pop();
        }

        if (_fixedColumnCount > 0)
        {
            var fixedClipW = Math.Min(fixedW, _viewportBodyWidth);
            var fixedClip = new Rect(_rowHeaderWidth, 0, fixedClipW, ScaledColumnHeaderHeight);
            dc.PushClip(new RectangleGeometry(fixedClip));
            var fx = _rowHeaderWidth;
            for (var col = 0; col < _fixedColumnCount; col++)
            {
                DrawColumnHeader(dc, col, fx, typeface);
                fx += GetColumnWidth(col);
            }

            dc.Pop();
        }

        DrawColumnMoveCue(dc);
        var rowHeaderClip = new Rect(0, ScaledColumnHeaderHeight, _rowHeaderWidth, _viewportBodyHeight);
        dc.PushClip(new RectangleGeometry(rowHeaderClip));
        {
            var rowHeaderPen = new Pen(GridLineBrush, GridPenThickness);
            ForEachVisibleRow(row =>
            {
                var rowHeight = GetRowHeight(row);
                var y = ScaledColumnHeaderHeight + GetRowBodyTopRel(row);
                var rect = new Rect(0, y, _rowHeaderWidth, rowHeight);
                var rowHeaderBackground = (IsRowHeaderMarkedSelected(row) && !HideHeaderSelectionColoring) ? SelectionBackground : HeaderBackground;
                dc.DrawRectangle(rowHeaderBackground, null, rect);
                // Top + bottom only; outer x=0 edge is one DrawLine in DrawOuterWorksheetFrame (avoids path vs line mismatch).
                dc.DrawLine(rowHeaderPen, new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top));
                dc.DrawLine(rowHeaderPen, new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Bottom));
                var visibleRect = Rect.Intersect(rect, rowHeaderClip);
                if (!visibleRect.IsEmpty)
                {
                    GriddoValuePainter.Paint(dc, row + 1, visibleRect, typeface, EffectiveFontSize, Brushes.Black, false, false, TextAlignment.Right, VerticalAlignment.Center);
                }
            });
        }
        dc.Pop();

        DrawHeaderFocusHighlight(dc);
        DrawOuterWorksheetFrame(dc);
    }

    private void DrawHeaderFocusHighlight(DrawingContext dc)
    {
        if (_headerFocusKind == HeaderFocusKind.None)
        {
            return;
        }

        var pen = new Pen(Brushes.Red, 2);
        pen.Freeze();
        var inset = pen.Thickness / 2;

        void DrawOutlinedRect(Rect rect)
        {
            if (rect.IsEmpty)
            {
                return;
            }

            var rr = new Rect(
                rect.X + inset,
                rect.Y + inset,
                Math.Max(0, rect.Width - pen.Thickness),
                Math.Max(0, rect.Height - pen.Thickness));
            if (rr.Width <= 0 || rr.Height <= 0)
            {
                return;
            }

            dc.DrawRectangle(null, pen, rr);
        }

        if (_headerFocusKind == HeaderFocusKind.Column && _columnHeaderRightClickOutline.Count > 0)
        {
            foreach (var col in _columnHeaderRightClickOutline.OrderBy(c => c))
            {
                DrawOutlinedRect(GetColumnHeaderRect(col));
            }

            return;
        }

        if (_headerFocusKind == HeaderFocusKind.Row && _rowHeaderRightClickOutline.Count > 0)
        {
            foreach (var row in _rowHeaderRightClickOutline.OrderBy(r => r))
            {
                DrawOutlinedRect(GetRowHeaderRect(row));
            }

            return;
        }

        Rect r = Rect.Empty;
        if (_headerFocusKind == HeaderFocusKind.Corner)
        {
            r = new Rect(0, 0, _rowHeaderWidth, ScaledColumnHeaderHeight);
        }
        else if (_headerFocusKind == HeaderFocusKind.Column
                 && _headerFocusColumnIndex >= 0
                 && _headerFocusColumnIndex < Columns.Count)
        {
            r = GetColumnHeaderRect(_headerFocusColumnIndex);
        }
        else if (_headerFocusKind == HeaderFocusKind.Row
                 && _headerFocusRowIndex >= 0
                 && _headerFocusRowIndex < Rows.Count)
        {
            r = GetRowHeaderRect(_headerFocusRowIndex);
        }

        DrawOutlinedRect(r);
    }

    /// <summary>
    /// Single DrawLine for the outermost grid edges so they match column header strokes (PathGeometry stroke looked thicker under AA).
    /// </summary>
    private void DrawOuterWorksheetFrame(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var pen = new Pen(GridLineBrush, GridPenThickness);
        var topRight = Math.Max(0, ActualWidth - ScrollBarSize);
        var stripBottom = ScaledColumnHeaderHeight + Math.Max(0, _viewportBodyHeight);
        var layoutBottom = Math.Max(0, ActualHeight - ScrollBarSize);
        var leftBottom = Math.Min(stripBottom, layoutBottom);
        var rightX = Math.Max(0, ActualWidth - 1);
        var bottomY = Math.Max(0, ActualHeight - 1);
        dc.DrawLine(pen, new Point(0, 0), new Point(topRight, 0));
        dc.DrawLine(pen, new Point(0, 0), new Point(0, leftBottom));
        dc.DrawLine(pen, new Point(rightX, 0), new Point(rightX, bottomY));
        dc.DrawLine(pen, new Point(0, bottomY), new Point(rightX, bottomY));
    }

    private void DrawBody(DrawingContext dc)
    {
        if (Rows.Count == 0)
        {
            return;
        }

        var bodyViewport = new Rect(_rowHeaderWidth, ScaledColumnHeaderHeight, _viewportBodyWidth, _viewportBodyHeight);
        var typeface = new Typeface("Segoe UI");
        var rowHeight = GetRowHeight(0);
        var fixedW = GetFixedColumnsWidth();
        var scrollLeft = _rowHeaderWidth + fixedW;

        if (_fixedColumnCount < Columns.Count && scrollLeft < _rowHeaderWidth + _viewportBodyWidth)
        {
            var scrollClipW = Math.Max(0, _viewportBodyWidth - fixedW);
            var scrollClip = new Rect(scrollLeft, ScaledColumnHeaderHeight, scrollClipW, _viewportBodyHeight);
            dc.PushClip(new RectangleGeometry(scrollClip));
            GetVisibleScrollColumnRange(out var sCol, out var eCol, out var startX);
            if (eCol >= sCol)
            {
                ForEachVisibleRow(row =>
                {
                    var y = ScaledColumnHeaderHeight + GetRowBodyTopRel(row);
                    var rowData = Rows[row];
                    var x = startX;
                    for (var col = sCol; col <= eCol; col++)
                    {
                        DrawBodyCell(dc, row, col, x, y, rowHeight, rowData, bodyViewport, typeface);
                        x += GetColumnWidth(col);
                    }
                });
            }

            dc.Pop();
        }

        if (_fixedColumnCount > 0)
        {
            var fixedClipW = Math.Min(fixedW, _viewportBodyWidth);
            var fixedClip = new Rect(_rowHeaderWidth, ScaledColumnHeaderHeight, fixedClipW, _viewportBodyHeight);
            dc.PushClip(new RectangleGeometry(fixedClip));
            ForEachVisibleRow(row =>
            {
                var y = ScaledColumnHeaderHeight + GetRowBodyTopRel(row);
                var rowData = Rows[row];
                var x = _rowHeaderWidth;
                for (var col = 0; col < _fixedColumnCount; col++)
                {
                    DrawBodyCell(dc, row, col, x, y, rowHeight, rowData, bodyViewport, typeface);
                    x += GetColumnWidth(col);
                }
            });

            dc.Pop();
        }

        DrawFixedRowSeparator(dc);
    }

    private void DrawFixedRowSeparator(DrawingContext dc)
    {
        if (GetScrollableRowsContentHeight() <= 1e-6)
        {
            return;
        }

        var f = GetEffectiveFixedRowCount();
        if (f <= 0)
        {
            return;
        }

        var h = GetRowHeight(0);
        var yLine = ScaledColumnHeaderHeight + f * h;
        if (yLine > ScaledColumnHeaderHeight + _viewportBodyHeight)
        {
            return;
        }

        var pen = new Pen(FixedRowBottomBorderBrush, GridPenThickness);
        dc.DrawLine(pen, new Point(_rowHeaderWidth, yLine), new Point(_rowHeaderWidth + _viewportBodyWidth, yLine));
    }
    private void DrawCurrentCellOverlay(DrawingContext dc)
    {
        if (!_currentCell.IsValid)
        {
            return;
        }

        var rect = GetCellRect(_currentCell.RowIndex, _currentCell.ColumnIndex);
        if (rect.IsEmpty)
        {
            return;
        }

        dc.PushClip(new RectangleGeometry(GetColumnBodyBandClipRect(_currentCell.ColumnIndex)));

        const double currentCellInset = 0.5;
        var insetRect = new Rect(
            rect.X + currentCellInset,
            rect.Y + currentCellInset,
            Math.Max(0, rect.Width - (currentCellInset * 2)),
            Math.Max(0, rect.Height - (currentCellInset * 2)));
        var isHostedEditMode = IsCurrentHostedCellInEditMode();
        var isEditCell = _isEditing || isHostedEditMode;
        if ((isEditCell && HideEditCellColor) || (!isEditCell && HideCurrentCellColor))
        {
            dc.Pop();
            return;
        }

        var borderBrush = isEditCell ? Brushes.Red : CurrentCellBorderBrush;
        dc.DrawRectangle(null, new Pen(borderBrush, ScaledCurrentCellBorder), insetRect);
        dc.Pop();
    }
    private void DrawEditingText(DrawingContext dc)
    {
        if (!_isEditing || !_currentCell.IsValid)
        {
            return;
        }

        var rect = GetCellRect(_currentCell.RowIndex, _currentCell.ColumnIndex);
        if (rect.IsEmpty)
        {
            return;
        }

        if (!TryGetCurrentColumn(out var column))
        {
            return;
        }

        dc.PushClip(new RectangleGeometry(GetColumnBodyBandClipRect(_currentCell.ColumnIndex)));

        // Keep editor visuals inside the cell border so the edit outline thickness stays consistent.
        const double editContentInset = 1.0;
        var editContentRect = new Rect(
            rect.X + editContentInset,
            rect.Y + editContentInset,
            Math.Max(0, rect.Width - (editContentInset * 2)),
            Math.Max(0, rect.Height - (editContentInset * 2)));

        if (column.IsHtml)
        {
            var bodyCellsViewport = new Rect(_rowHeaderWidth, ScaledColumnHeaderHeight, _viewportBodyWidth, _viewportBodyHeight);
            editContentRect = Rect.Intersect(editContentRect, bodyCellsViewport);
        }

        if (editContentRect.IsEmpty)
        {
            dc.Pop();
            return;
        }

        dc.DrawRectangle(Brushes.White, null, editContentRect);
        var typeface = new Typeface("Segoe UI");
        var fontSize = EffectiveFontSize;
        var verticalAlignment = VerticalAlignment.Center;
        // Edit mode should show the literal source text (including HTML markup), not rendered HTML.
        GriddoValuePainter.Paint(dc, _editSession.Buffer, editContentRect, typeface, fontSize, Brushes.Black, false, false, column.ContentAlignment, verticalAlignment);

        var displayText = _editSession.Buffer;
        var editText = new FormattedText(
            displayText,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            1.0);
        editText.TextAlignment = column.ContentAlignment;
        editText.MaxTextWidth = Math.Max(1, editContentRect.Width - 8);
        editText.MaxTextHeight = Math.Max(1, editContentRect.Height - 4);
        editText.Trimming = TextTrimming.CharacterEllipsis;
        var caretOriginY = verticalAlignment == VerticalAlignment.Center
            ? editContentRect.Y + Math.Max(0, (editContentRect.Height - editText.Height) / 2)
            : editContentRect.Y + 2;
        var prefixText = displayText[.._editSession.CaretIndex];
        var prefixFormattedText = new FormattedText(
            prefixText,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            1.0);
        var contentWidth = Math.Max(1, editContentRect.Width - 8);
        var totalTextWidth = Math.Min(editText.WidthIncludingTrailingWhitespace, contentWidth);
        var textStartX = editContentRect.X + 4;
        if (column.ContentAlignment == TextAlignment.Right)
        {
            textStartX += Math.Max(0, contentWidth - totalTextWidth);
        }
        else if (column.ContentAlignment == TextAlignment.Center)
        {
            textStartX += Math.Max(0, (contentWidth - totalTextWidth) / 2);
        }

        if (_editSession.TryGetSelection(out var selectionStart, out var selectionEnd))
        {
            if (column.IsHtml)
            {
                var selectionGeometry = editText.BuildHighlightGeometry(new Point(textStartX, caretOriginY), selectionStart, selectionEnd - selectionStart);
                if (selectionGeometry is not null && !selectionGeometry.Bounds.IsEmpty)
                {
                    dc.PushClip(new RectangleGeometry(editContentRect));
                    dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(120, 102, 178, 255)), null, selectionGeometry);
                    GriddoValuePainter.Paint(dc, _editSession.Buffer, editContentRect, typeface, fontSize, Brushes.Black, false, false, column.ContentAlignment, verticalAlignment);
                    dc.Pop();
                }
            }
            else
            {
                var beforeSelection = displayText[..selectionStart];
                var selectedText = displayText[selectionStart..selectionEnd];
                var beforeWidth = new FormattedText(
                    beforeSelection,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    Brushes.Black,
                    1.0).WidthIncludingTrailingWhitespace;
                var selectedWidth = new FormattedText(
                    selectedText,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    Brushes.Black,
                    1.0).WidthIncludingTrailingWhitespace;
                var selectionX = Math.Clamp(textStartX + Math.Min(beforeWidth, contentWidth), editContentRect.X + 2, editContentRect.Right - 2);
                var selectionRight = Math.Clamp(selectionX + Math.Min(selectedWidth, contentWidth), editContentRect.X + 2, editContentRect.Right - 2);
                if (selectionRight > selectionX)
                {
                    var selectionRect = new Rect(selectionX, caretOriginY, selectionRight - selectionX, Math.Max(1, editText.Height));
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(120, 102, 178, 255)), null, selectionRect);
                    GriddoValuePainter.Paint(dc, _editSession.Buffer, editContentRect, typeface, fontSize, Brushes.Black, false, false, column.ContentAlignment, verticalAlignment);
                }
            }
        }

        var textOrigin = new Point(textStartX, caretOriginY);
        var caretX = textStartX;
        var caretTop = caretOriginY;
        var caretBottom = Math.Min(editContentRect.Bottom - 2, caretOriginY + editText.Height);
        if (TryGetCaretBounds(editText, textOrigin, _editSession.CaretIndex, out var caretBounds))
        {
            caretX = caretBounds.X;
            caretTop = caretBounds.Top;
            caretBottom = caretBounds.Bottom;
        }

        caretX = Math.Clamp(caretX, editContentRect.X + 2, editContentRect.Right - 2);
        caretTop = Math.Clamp(caretTop, editContentRect.Y + 1, editContentRect.Bottom - 1);
        caretBottom = Math.Clamp(caretBottom, caretTop + 1, editContentRect.Bottom - 1);
        if (caretBottom > caretTop)
        {
            dc.DrawLine(new Pen(Brushes.Black, 1), new Point(caretX, caretTop), new Point(caretX, caretBottom));
        }

        dc.Pop();
    }

    private static bool TryGetCaretBounds(FormattedText formattedText, Point origin, int caretIndex, out Rect bounds)
    {
        bounds = Rect.Empty;
        var text = formattedText.Text ?? string.Empty;
        if (text.Length == 0)
        {
            return false;
        }

        var clampedCaret = Math.Clamp(caretIndex, 0, text.Length);
        if (clampedCaret < text.Length)
        {
            var geometryAtCaret = formattedText.BuildHighlightGeometry(origin, clampedCaret, 1);
            if (geometryAtCaret is not null && !geometryAtCaret.Bounds.IsEmpty)
            {
                bounds = geometryAtCaret.Bounds;
                bounds = new Rect(bounds.Left, bounds.Top, 0, Math.Max(1, bounds.Height));
                return true;
            }
        }

        if (clampedCaret > 0)
        {
            var geometryAtPrevious = formattedText.BuildHighlightGeometry(origin, clampedCaret - 1, 1);
            if (geometryAtPrevious is not null && !geometryAtPrevious.Bounds.IsEmpty)
            {
                var prev = geometryAtPrevious.Bounds;
                bounds = new Rect(prev.Right, prev.Top, 0, Math.Max(1, prev.Height));
                return true;
            }
        }

        return false;
    }
    private void DrawColumnMoveCue(DrawingContext dc)
    {
        if (!_isTrackingColumnMove)
        {
            return;
        }

        var clipRect = new Rect(_rowHeaderWidth, 0, _viewportBodyWidth, ScaledColumnHeaderHeight);

        // Keep a thin red "current/source" marker on the column(s) being moved.
        if (_isMovingPointerInColumnHeader && _movingColumnIndex >= 0 && _movingColumnIndex < Columns.Count)
        {
            var currentPen = new Pen(Brushes.Red, 1);
            var movingColumns = _columnMoveStartedFromSelectedHeader && _isMovingColumn
                ? GetSelectedColumnIndices()
                : [_movingColumnIndex];
            foreach (var movingColumn in movingColumns)
            {
                if (movingColumn < 0 || movingColumn >= Columns.Count)
                {
                    continue;
                }

                var movingRect = GetColumnHeaderRect(movingColumn);
                var visibleMovingRect = Rect.Intersect(movingRect, clipRect);
                if (visibleMovingRect.IsEmpty)
                {
                    continue;
                }

                var currentRect = new Rect(
                    visibleMovingRect.X + 0.5,
                    visibleMovingRect.Y + 0.5,
                    Math.Max(0, visibleMovingRect.Width - 1),
                    Math.Max(0, visibleMovingRect.Height - 1));
                dc.DrawRectangle(null, currentPen, currentRect);
            }
        }

        if (_columnMoveCueIndex < 0 || _columnMoveCueIndex >= Columns.Count)
        {
            return;
        }

        var cueRect = GetColumnHeaderRect(_columnMoveCueIndex);
        if (cueRect.IsEmpty)
        {
            return;
        }

        var visibleCueRect = Rect.Intersect(cueRect, clipRect);
        if (visibleCueRect.IsEmpty)
        {
            return;
        }

        var x = visibleCueRect.Left;
        if (_columnMoveStartedFromSelectedHeader && _isMovingColumn)
        {
            var selectedColumns = GetSelectedColumnIndices();
            if (selectedColumns.Count > 0)
            {
                var minSelected = selectedColumns[0];
                var maxSelected = selectedColumns[^1];
                var movingLeft = _columnMoveCueIndex < minSelected;
                var movingRight = _columnMoveCueIndex > maxSelected;
                x = movingRight ? visibleCueRect.Right : visibleCueRect.Left;
            }
        }
        else
        {
            var movingRight = _movingColumnIndex >= 0 && _columnMoveCueIndex > _movingColumnIndex;
            x = movingRight ? visibleCueRect.Right : visibleCueRect.Left;
        }
        var insertionPen = new Pen(Brushes.Red, 2);
        dc.DrawLine(
            insertionPen,
            new Point(x, 1),
            new Point(x, Math.Max(1, ScaledColumnHeaderHeight - 1)));

        DrawDropArrows(dc, x, ScaledColumnHeaderHeight);
    }

    private void DrawRowMoveCue(DrawingContext dc)
    {
        if (!_isTrackingRowMove || !_isMovingRow)
        {
            return;
        }

        var clipRect = new Rect(0, ScaledColumnHeaderHeight, _rowHeaderWidth, _viewportBodyHeight);
        var currentPen = new Pen(Brushes.Red, 1);
        var movingRows = GetSelectedRowIndices();
        foreach (var movingRow in movingRows)
        {
            var movingRect = GetRowHeaderRect(movingRow);
            var visibleMovingRect = Rect.Intersect(movingRect, clipRect);
            if (visibleMovingRect.IsEmpty)
            {
                continue;
            }

            var currentRect = new Rect(
                visibleMovingRect.X + 0.5,
                visibleMovingRect.Y + 0.5,
                Math.Max(0, visibleMovingRect.Width - 1),
                Math.Max(0, visibleMovingRect.Height - 1));
            dc.DrawRectangle(null, currentPen, currentRect);
        }

        if (_rowMoveCueIndex < 0 || _rowMoveCueIndex >= Rows.Count)
        {
            return;
        }

        var cueRect = GetRowHeaderRect(_rowMoveCueIndex);
        if (cueRect.IsEmpty)
        {
            return;
        }

        if (!TryGetRowDropIndicatorY(out var y))
        {
            return;
        }

        y = Math.Clamp(y, clipRect.Top + 1, clipRect.Bottom - 1);
        var insertionPen = new Pen(Brushes.Red, 2);
        dc.DrawLine(
            insertionPen,
            new Point(1, y),
            new Point(Math.Max(1, _rowHeaderWidth - 1), y));

        DrawRowDropArrows(dc, y, _rowHeaderWidth, clipRect.Top + 1, clipRect.Bottom - 1);
    }

    private bool TryGetRowDropIndicatorY(out double y)
    {
        y = 0;
        if (_rowMoveCueIndex < 0 || _rowMoveCueIndex >= Rows.Count)
        {
            return false;
        }

        var selectedRows = GetSelectedRowIndices();
        if (selectedRows.Count == 0)
        {
            if (_movingRowIndex < 0 || _movingRowIndex >= Rows.Count || _rowMoveCueIndex == _movingRowIndex)
            {
                return false;
            }

            var cueRectSingle = GetRowHeaderRect(_rowMoveCueIndex);
            if (cueRectSingle.IsEmpty)
            {
                return false;
            }

            y = _rowMoveCueIndex > _movingRowIndex ? cueRectSingle.Bottom : cueRectSingle.Top;
            return true;
        }

        var minSelected = selectedRows[0];
        var maxSelected = selectedRows[^1];
        if (_rowMoveCueIndex >= minSelected && _rowMoveCueIndex <= maxSelected)
        {
            return false;
        }

        var cueRect = GetRowHeaderRect(_rowMoveCueIndex);
        if (cueRect.IsEmpty)
        {
            return false;
        }

        var insertAfterTarget = _rowMoveCueIndex > maxSelected;
        y = insertAfterTarget ? cueRect.Bottom : cueRect.Top;
        return true;
    }

    private static void DrawDropArrows(DrawingContext dc, double lineX, double headerHeight)
    {
        const double arrowWidth = 6;
        const double arrowHeight = 4;
        const double gap = 3;
        var centerY = headerHeight / 2.0;

        var red = Brushes.Red;

        // Left arrow pointing right.
        var leftArrow = new StreamGeometry();
        using (var ctx = leftArrow.Open())
        {
            var tip = new Point(lineX - gap, centerY);
            ctx.BeginFigure(new Point(tip.X - arrowWidth, tip.Y - arrowHeight), true, true);
            ctx.LineTo(new Point(tip.X - arrowWidth, tip.Y + arrowHeight), true, false);
            ctx.LineTo(tip, true, false);
        }
        leftArrow.Freeze();
        dc.DrawGeometry(red, null, leftArrow);

        // Right arrow pointing left.
        var rightArrow = new StreamGeometry();
        using (var ctx = rightArrow.Open())
        {
            var tip = new Point(lineX + gap, centerY);
            ctx.BeginFigure(new Point(tip.X + arrowWidth, tip.Y - arrowHeight), true, true);
            ctx.LineTo(new Point(tip.X + arrowWidth, tip.Y + arrowHeight), true, false);
            ctx.LineTo(tip, true, false);
        }
        rightArrow.Freeze();
        dc.DrawGeometry(red, null, rightArrow);
    }

    private static void DrawRowDropArrows(DrawingContext dc, double lineY, double headerWidth, double minY, double maxY)
    {
        const double arrowWidth = 4;
        const double arrowHeight = 6;
        const double gap = 3;
        var clampedLineY = Math.Clamp(lineY, minY, maxY);
        var centerX = headerWidth / 2.0;
        var red = Brushes.Red;

        // Down arrow above insertion line.
        var downArrow = new StreamGeometry();
        using (var ctx = downArrow.Open())
        {
            var tipY = Math.Clamp(clampedLineY - gap, minY + arrowHeight, maxY - arrowHeight);
            var tip = new Point(centerX, tipY);
            ctx.BeginFigure(new Point(tip.X - arrowWidth, tip.Y - arrowHeight), true, true);
            ctx.LineTo(new Point(tip.X + arrowWidth, tip.Y - arrowHeight), true, false);
            ctx.LineTo(tip, true, false);
        }
        downArrow.Freeze();
        dc.DrawGeometry(red, null, downArrow);

        // Up arrow below insertion line.
        var upArrow = new StreamGeometry();
        using (var ctx = upArrow.Open())
        {
            var tipY = Math.Clamp(clampedLineY + gap, minY + arrowHeight, maxY - arrowHeight);
            var tip = new Point(centerX, tipY);
            ctx.BeginFigure(new Point(tip.X - arrowWidth, tip.Y + arrowHeight), true, true);
            ctx.LineTo(new Point(tip.X + arrowWidth, tip.Y + arrowHeight), true, false);
            ctx.LineTo(tip, true, false);
        }
        upArrow.Freeze();
        dc.DrawGeometry(red, null, upArrow);
    }

    private Rect GetColumnHeaderRect(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= Columns.Count)
        {
            return Rect.Empty;
        }

        double left;
        if (columnIndex < _fixedColumnCount)
        {
            left = _rowHeaderWidth;
            for (var col = 0; col < columnIndex; col++)
            {
                left += GetColumnWidth(col);
            }
        }
        else
        {
            left = _rowHeaderWidth + GetFixedColumnsWidth();
            for (var col = _fixedColumnCount; col < columnIndex; col++)
            {
                left += GetColumnWidth(col);
            }

            left -= _horizontalOffset;
        }

        return new Rect(left, 0, GetColumnWidth(columnIndex), ScaledColumnHeaderHeight);
    }

    private Rect GetRowHeaderRect(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= Rows.Count)
        {
            return Rect.Empty;
        }

        var y = ScaledColumnHeaderHeight + GetRowBodyTopRel(rowIndex);
        return new Rect(0, y, _rowHeaderWidth, GetRowHeight(rowIndex));
    }
    private void DrawScrollBarCorner(DrawingContext dc)
    {
        const double outerBorderInset = 1;
        var cornerThickness = Math.Max(0, ScrollBarSize - outerBorderInset);
        var topRightRect = new Rect(
            Math.Max(0, ActualWidth - ScrollBarSize - outerBorderInset),
            0,
            cornerThickness,
            Math.Max(0, ScaledColumnHeaderHeight));
        var bottomLeftRect = new Rect(
            0,
            Math.Max(0, ActualHeight - ScrollBarSize - outerBorderInset),
            Math.Max(0, _rowHeaderWidth),
            cornerThickness);
        var bottomRightRect = new Rect(
            Math.Max(0, ActualWidth - ScrollBarSize - outerBorderInset),
            Math.Max(0, ActualHeight - ScrollBarSize - outerBorderInset),
            cornerThickness,
            cornerThickness);

        dc.DrawRectangle(HeaderBackground, null, topRightRect);
        dc.DrawRectangle(HeaderBackground, null, bottomLeftRect);
        dc.DrawRectangle(HeaderBackground, null, bottomRightRect);

        var pen = new Pen(GridLineBrush, GridPenThickness);

        // Top-right: only top border.
        dc.DrawLine(
            pen,
            new Point(topRightRect.Left, topRightRect.Top),
            new Point(topRightRect.Right, topRightRect.Top));

        // Bottom-left: only left border.
        dc.DrawLine(
            pen,
            new Point(bottomLeftRect.Left, bottomLeftRect.Top),
            new Point(bottomLeftRect.Left, bottomLeftRect.Bottom));

        // Bottom-right: keep subtle separator to both scrollbars.
        dc.DrawLine(
            pen,
            new Point(bottomRightRect.Left, bottomRightRect.Top),
            new Point(bottomRightRect.Right, bottomRightRect.Top));
        dc.DrawLine(
            pen,
            new Point(bottomRightRect.Left, bottomRightRect.Top),
            new Point(bottomRightRect.Left, bottomRightRect.Bottom));
    }

}
