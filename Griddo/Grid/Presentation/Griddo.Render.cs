using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Linq;
using Griddo.Fields;
using Griddo.Primitives;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    private void InvalidateCachedMetricPens()
    {
        _cachedGridLinePen = null;
        _cachedGridLinePenBrush = null;
        _cachedGridLinePenThickness = double.NaN;
        _cachedFixedFieldRightPen = null;
        _cachedFixedFieldRightPenBrush = null;
        _cachedFixedFieldRightPenThickness = double.NaN;
        _cachedFixedRecordBottomPen = null;
        _cachedFixedRecordBottomPenBrush = null;
        _cachedFixedRecordBottomPenThickness = double.NaN;
    }

    private Pen ResolveGridLinePen()
    {
        var thickness = GridPenThickness;
        var brush = GridLineBrush;
        if (_cachedGridLinePen is null
            || !ReferenceEquals(_cachedGridLinePenBrush, brush)
            || Math.Abs(_cachedGridLinePenThickness - thickness) > 1e-9)
        {
            _cachedGridLinePen = new Pen(brush, thickness);
            _cachedGridLinePenBrush = brush;
            _cachedGridLinePenThickness = thickness;
        }

        return _cachedGridLinePen;
    }

    private Pen ResolveFixedFieldRightPen()
    {
        var thickness = GridPenThickness;
        var brush = FixedFieldRightBorderBrush;
        if (_cachedFixedFieldRightPen is null
            || !ReferenceEquals(_cachedFixedFieldRightPenBrush, brush)
            || Math.Abs(_cachedFixedFieldRightPenThickness - thickness) > 1e-9)
        {
            _cachedFixedFieldRightPen = new Pen(brush, thickness);
            _cachedFixedFieldRightPenBrush = brush;
            _cachedFixedFieldRightPenThickness = thickness;
        }

        return _cachedFixedFieldRightPen;
    }

    private Pen ResolveFixedRecordBottomPen()
    {
        var thickness = GridPenThickness;
        var brush = FixedRecordBottomBorderBrush;
        if (_cachedFixedRecordBottomPen is null
            || !ReferenceEquals(_cachedFixedRecordBottomPenBrush, brush)
            || Math.Abs(_cachedFixedRecordBottomPenThickness - thickness) > 1e-9)
        {
            _cachedFixedRecordBottomPen = new Pen(brush, thickness);
            _cachedFixedRecordBottomPenBrush = brush;
            _cachedFixedRecordBottomPenThickness = thickness;
        }

        return _cachedFixedRecordBottomPen;
    }

    private bool ShouldShowSelectionVisuals()
    {
        return !HideSelectionWhenGridLosesFocus || IsKeyboardFocusWithin;
    }

    private Brush ResolveHeaderForeground(bool isSelected)
    {
        return isSelected ? HeaderSelectionForeground : HeaderForeground;
    }

    protected override void OnRender(DrawingContext dc)
    {
        SyncHostedCells();
        dc.DrawRectangle(BodyBackground, null, new Rect(0, 0, ActualWidth, ActualHeight));
        DrawHeaders(dc);
        DrawBody(dc);
        DrawEditingText(dc);
        DrawCurrentCellOverlay(dc);
        DrawRecordMoveCue(dc);
        DrawScrollBarCorner(dc);
    }

    private FontWeight ResolveFieldHeaderFontWeight(int col)
    {
        if (Fields[col] is IGriddoHostedFieldView)
        {
            return FontWeights.Bold;
        }

        if (Fields[col] is IGriddoFieldEditableHeaderView h && h.UseBoldColumnHeader)
        {
            return FontWeights.Bold;
        }

        return HeaderFontWeight;
    }

    private void DrawFieldHeader(DrawingContext dc, int col, double x, Typeface typeface, Rect? clipRect = null)
    {
        var width = GetFieldWidth(col);
        var rect = new Rect(x, 0, width, ScaledFieldHeaderHeight);
        var isSelectedHeader = ShouldShowSelectionVisuals() && IsFieldHeaderMarkedSelected(col) && ShowFieldHeaderSelectionColoring;
        var headerBackground = isSelectedHeader ? HeaderSelectionBackground : HeaderBackground;
        dc.DrawRectangle(headerBackground, null, rect);
        var pen = ResolveGridLinePen();
        // Top edge of header strip is drawn once in DrawOuterWorksheetFrame (matches DrawLine rasterization for scroll fields).
        dc.DrawLine(pen, rect.TopRight, rect.BottomRight);
        if (col == 0)
        {
            dc.DrawLine(pen, rect.TopLeft, rect.BottomLeft);
        }
        var headerLabel = Fields[col] is IGriddoFieldTitleView titleView && !string.IsNullOrWhiteSpace(titleView.AbbreviatedHeader)
            ? titleView.AbbreviatedHeader
            : Fields[col].Header;
        var headerText = new FormattedText(
            headerLabel,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            EffectiveFontSize,
            ResolveHeaderForeground(isSelectedHeader),
            1.0);
        headerText.SetFontWeight(ResolveFieldHeaderFontWeight(col));
        headerText.TextAlignment = TextAlignment.Center;
        var visibleRect = clipRect.HasValue ? Rect.Intersect(rect, clipRect.Value) : rect;
        if (visibleRect.IsEmpty)
        {
            return;
        }

        headerText.MaxTextWidth = Math.Max(1, visibleRect.Width - 8);
        headerText.MaxTextHeight = Math.Max(1, visibleRect.Height - 4);
        headerText.Trimming = TextTrimming.CharacterEllipsis;
        var headerY = visibleRect.Y + Math.Max(0, (visibleRect.Height - headerText.Height) / 2);
        dc.DrawText(headerText, new Point(visibleRect.X + 4, headerY));
        DrawSortHeaderIndicator(dc, col, rect, typeface);

        if (_fixedFieldCount > 0 && col == _fixedFieldCount - 1)
        {
            dc.DrawLine(
                ResolveFixedFieldRightPen(),
                new Point(rect.Right, rect.Top),
                new Point(rect.Right, rect.Bottom));
        }
    }

    private void DrawSortHeaderIndicator(DrawingContext dc, int col, Rect rect, Typeface typeface)
    {
        if (!ShowSortingIndicators)
        {
            return;
        }

        var priority = TryGetSortPriorityForField(col, out var ascending);
        if (priority <= 0)
        {
            return;
        }

        var arrecordText = ascending ? "▲" : "▼";
        var indicatorBrush = new SolidColorBrush(Color.FromRgb(120, 120, 120));
        var arrecord = new FormattedText(
            arrecordText,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            Math.Max(7.0, EffectiveFontSize * 0.7),
            indicatorBrush,
            1.0);
        var prio = new FormattedText(
            priority.ToString(System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            Math.Max(8.0, EffectiveFontSize * 0.65),
            indicatorBrush,
            1.0);
        prio.SetFontWeight(FontWeights.Bold);

        var right = rect.Right - 3;
        dc.DrawText(prio, new Point(right - prio.Width, rect.Top + 1));
        dc.DrawText(arrecord, new Point(right - Math.Max(arrecord.Width, prio.Width), rect.Top + 1 + prio.Height));
    }

    private void DrawBodyCell(
        DrawingContext dc,
        int record,
        int col,
        double x,
        double y,
        double recordHeight,
        object recordData,
        Rect bodyViewport,
        Typeface typeface)
    {
        var colWidth = GetFieldWidth(col);
        var cellW = colWidth;
        var cellH = recordHeight;
        if (IsBodyTransposed)
        {
            cellW = GetRecordHeight(record);
            cellH = colWidth;
        }

        var rect = new Rect(x, y, cellW, cellH);
        var address = new GriddoCellAddress(record, col);
        var mergedRenderRect = GetMergedRecordCellRect(record, col, rect);
        var mergeBand = Fields[col] as IGriddoRecordMergeBandView;
        var mergedWithPrevEarly = !IsBodyTransposed
            && mergeBand?.IsMergedWithPreviousRecord(Records, record) == true;
        var mergedWithNextEarly = !IsBodyTransposed
            && mergeBand?.IsMergedWithNextRecord(Records, record) == true;
        var isMergedBandCellEarly = mergedWithPrevEarly || mergedWithNextEarly;
        var mergedPaintRectEarly = isMergedBandCellEarly ? mergedRenderRect : rect;
        var isMergedRenderCarrier = true;
        if (isMergedBandCellEarly)
        {
            var visibleMergedRect = Rect.Intersect(mergedRenderRect, bodyViewport);
            var midY = visibleMergedRect.Y + (visibleMergedRect.Height / 2.0);
            isMergedRenderCarrier = !visibleMergedRect.IsEmpty
                && midY >= rect.Top
                && midY < rect.Bottom;
        }

        var cellView = ResolveCellPropertyView(recordData, col);
        var isHostedCellEditing = IsHostedCellInEditMode(address);
        if ((!isMergedBandCellEarly || isMergedRenderCarrier) && TryGetFieldBackgroundBrush(col, recordData, cellView, out var fieldBackgroundBrush))
        {
            dc.DrawRectangle(fieldBackgroundBrush, null, mergedPaintRectEarly);
        }

        if ((!isMergedBandCellEarly || isMergedRenderCarrier) && _findMatchedCells.Contains(address))
        {
            dc.DrawRectangle(FindMatchBackground, null, mergedPaintRectEarly);
        }

        var isSelectedVisual = false;
        if ((!isMergedBandCellEarly || isMergedRenderCarrier) && !isHostedCellEditing && ShowCellSelectionColoring && ShouldShowSelectionVisuals())
        {
            var drawSelection = _selectedCells.Contains(address);
            if (mergeBand is not null && !IsBodyTransposed)
            {
                drawSelection = false;
                var top = record;
                while (top > 0 && mergeBand.IsMergedWithPreviousRecord(Records, top))
                {
                    top--;
                }

                var bottom = record;
                while (bottom < Records.Count - 1 && mergeBand.IsMergedWithNextRecord(Records, bottom))
                {
                    bottom++;
                }

                for (var r = top; r <= bottom; r++)
                {
                    if (_selectedCells.Contains(new GriddoCellAddress(r, col)))
                    {
                        drawSelection = true;
                        break;
                    }
                }
            }

            if (drawSelection)
            {
                dc.DrawRectangle(SelectionBackground, null, mergedRenderRect);
                isSelectedVisual = true;
            }
        }

        var mergedWithPrev = mergedWithPrevEarly;
        var mergedWithNext = mergedWithNextEarly;
        var gridPen = ResolveGridLinePen();
        if (!mergedWithPrev && !mergedWithNext)
        {
            dc.DrawRectangle(null, gridPen, rect);
        }
        else
        {
            dc.DrawLine(gridPen, rect.TopLeft, rect.BottomLeft);
            dc.DrawLine(gridPen, rect.TopRight, rect.BottomRight);
            if (!mergedWithPrev)
            {
                dc.DrawLine(gridPen, rect.TopLeft, rect.TopRight);
            }

            if (!mergedWithNext)
            {
                dc.DrawLine(gridPen, rect.BottomLeft, rect.BottomRight);
            }
        }
        if (!IsBodyTransposed && _fixedFieldCount > 0 && col == _fixedFieldCount - 1)
        {
            dc.DrawLine(
                ResolveFixedFieldRightPen(),
                new Point(rect.Right, rect.Top),
                new Point(rect.Right, rect.Bottom));
        }

        if (IsBodyTransposed && _fixedFieldCount > 0 && col == _fixedFieldCount - 1)
        {
            dc.DrawLine(
                ResolveFixedFieldRightPen(),
                new Point(rect.Left, rect.Bottom),
                new Point(rect.Right, rect.Bottom));
        }

        // For vertically merged bands, render content only in the record that
        // contains the vertical center of the visible merged area.
        if (isMergedBandCellEarly && !isMergedRenderCarrier)
        {
            return;
        }

        if (Fields[col] is IGriddoHostedFieldView)
        {
            return;
        }

        var fieldTypeface = ResolveFieldTypeface(col, typeface, cellView);
        var fieldFontSize = ResolveFieldFontSize(col, cellView);
        var foregroundBrush = ResolveFieldForegroundBrush(col, recordData, cellView);
        var underline = HasUnderlineStyle(col, cellView);
        var noWrap = HasNoWrapStyle(col, cellView);

        if (Fields[col] is IGriddoCheckboxToggleFieldView toggleCol && toggleCol.IsCheckboxCell(recordData))
        {
            var rawBool = Fields[col].GetValue(recordData);
            var isChecked = rawBool is true;
            var boolPaintBounds = Rect.Intersect(rect, bodyViewport);
            if (!boolPaintBounds.IsEmpty)
            {
                GriddoValuePainter.DrawBoolCheckbox(dc, isChecked, boolPaintBounds, fieldFontSize);
            }

            return;
        }

        var rawValue = Fields[col].GetValue(recordData);
        var isGraphic = rawValue is ImageSource or Geometry;
        var paintValue = (!isGraphic && !Fields[col].IsHtml)
            ? FormatCellValue(rawValue, col, cellView)
            : rawValue;
        // Intersect with viewport so HTML (and plain text) centers in the visible strip when the record is clipped vertically.
        var paintBounds = isGraphic ? mergedRenderRect : Rect.Intersect(mergedRenderRect, bodyViewport);
        if (!paintBounds.IsEmpty)
        {
            GriddoValuePainter.Paint(
                dc,
                paintValue,
                paintBounds,
                fieldTypeface,
                fieldFontSize,
                foregroundBrush,
                underline,
                Fields[col].IsHtml,
                true,
                Fields[col].ContentAlignment,
                isGraphic ? VerticalAlignment.Top : VerticalAlignment.Center,
                noWrap,
                renderHtmlBackground: !isSelectedVisual);
        }
    }

    private GriddoCellPropertyView? ResolveCellPropertyView(object recordData, int col)
    {
        if (CellPropertyViewResolver is null)
        {
            return null;
        }

        try
        {
            return CellPropertyViewResolver(recordData, col);
        }
        catch
        {
            return null;
        }
    }

    private object? FormatCellValue(object? rawValue, int col, GriddoCellPropertyView? cellView)
    {
        var format = cellView?.FormatString?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(format) && rawValue is IFormattable fmt)
        {
            try
            {
                return fmt.ToString(format, CultureInfo.CurrentCulture);
            }
            catch (FormatException)
            {
                // Fall back to the field's formatting.
            }
        }

        return Fields[col].FormatValue(rawValue);
    }

    private Typeface ResolveFieldTypeface(int col, Typeface fallback, GriddoCellPropertyView? cellView)
    {
        if (col < 0 || col >= Fields.Count)
        {
            return fallback;
        }

        var source = Fields[col];
        var overrideFamily = cellView?.FontFamilyName?.Trim() ?? string.Empty;
        var overrideStyle = cellView?.FontStyleName?.Trim() ?? string.Empty;
        var hasFamily = overrideFamily.Length > 0 || source is IGriddoFieldFontView { FontFamilyName: { Length: > 0 } };
        var hasStyle = overrideStyle.Length > 0 || source is IGriddoFieldFontView { FontStyleName: { Length: > 0 } };
        if (!hasFamily && !hasStyle)
        {
            return fallback;
        }

        var family = overrideFamily.Length > 0
            ? overrideFamily
            : source is IGriddoFieldFontView familyView && !string.IsNullOrWhiteSpace(familyView.FontFamilyName)
                ? familyView.FontFamilyName
            : fallback.FontFamily.Source;
        var styleName = overrideStyle.Length > 0
            ? overrideStyle
            : source is IGriddoFieldFontView styleView ? styleView.FontStyleName : string.Empty;
        var style = ParseFontStyle(styleName);
        var weight = ParseFontWeight(styleName);
        try
        {
            return new Typeface(new FontFamily(family), style, weight, fallback.Stretch);
        }
        catch
        {
            return fallback;
        }
    }

    private double ResolveFieldFontSize(int col, GriddoCellPropertyView? cellView)
    {
        if (col < 0 || col >= Fields.Count)
        {
            return EffectiveFontSize;
        }

        if (cellView is { FontSize: > 0 })
        {
            return Math.Max(6, cellView.FontSize * ContentScale);
        }

        if (Fields[col] is IGriddoFieldFontView { FontSize: > 0 } fontView)
        {
            return Math.Max(6, fontView.FontSize * ContentScale);
        }

        return EffectiveFontSize;
    }

    private Brush ResolveFieldForegroundBrush(int col, object recordData, GriddoCellPropertyView? cellView)
    {
        if (col < 0 || col >= Fields.Count)
        {
            return BodyForeground;
        }

        if (cellView is { ForegroundColor.Length: > 0 }
            && TryParseBrush(cellView.ForegroundColor, out var overrideBrush))
        {
            return overrideBrush;
        }

        if (Fields[col] is IGriddoDynamicFieldColorView dynamicColorView
            && TryParseBrush(dynamicColorView.GetForegroundColor(recordData), out var dynamicBrush))
        {
            return dynamicBrush;
        }

        if (Fields[col] is IGriddoFieldColorView colorView
            && TryParseBrush(colorView.ForegroundColor, out var brush))
        {
            return brush;
        }

        return BodyForeground;
    }

    private bool TryGetFieldBackgroundBrush(int col, object recordData, GriddoCellPropertyView? cellView, out Brush brush)
    {
        brush = Brushes.Transparent;
        if (col < 0 || col >= Fields.Count)
        {
            return false;
        }

        if (cellView is { BackgroundColor.Length: > 0 }
            && TryParseBrush(cellView.BackgroundColor, out var overrideBrush))
        {
            brush = overrideBrush;
            return true;
        }

        if (Fields[col] is IGriddoDynamicFieldColorView dynamicColorView
            && TryParseBrush(dynamicColorView.GetBackgroundColor(recordData), out var dynamicBrush))
        {
            brush = dynamicBrush;
            return true;
        }

        if (Fields[col] is IGriddoFieldColorView colorView
            && TryParseBrush(colorView.BackgroundColor, out var parsed))
        {
            brush = parsed;
            return true;
        }

        return false;
    }

    private static bool TryParseBrush(string text, out Brush brush)
    {
        brush = Brushes.Transparent;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            var converted = new BrushConverter().ConvertFromString(text);
            if (converted is Brush parsed)
            {
                brush = parsed;
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static FontStyle ParseFontStyle(string style)
    {
        var normalized = NormalizeStyleText(style);
        if (normalized.Contains("italic", StringComparison.Ordinal))
        {
            return FontStyles.Italic;
        }

        if (normalized.Contains("oblique", StringComparison.Ordinal))
        {
            return FontStyles.Oblique;
        }

        return FontStyles.Normal;
    }

    private static FontWeight ParseFontWeight(string style)
    {
        var normalized = NormalizeStyleText(style);
        if (normalized.Contains("bold", StringComparison.Ordinal))
        {
            return FontWeights.Bold;
        }

        return FontWeights.Normal;
    }

    private bool HasUnderlineStyle(int col, GriddoCellPropertyView? cellView)
    {
        var overrideStyle = cellView?.FontStyleName?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(overrideStyle))
        {
            return NormalizeStyleText(overrideStyle).Contains("underline", StringComparison.Ordinal);
        }

        if (col < 0 || col >= Fields.Count || Fields[col] is not IGriddoFieldFontView fontView)
        {
            return false;
        }

        return NormalizeStyleText(fontView.FontStyleName).Contains("underline", StringComparison.Ordinal);
    }

    private bool HasNoWrapStyle(int col, GriddoCellPropertyView? cellView)
    {
        if (cellView is { NoWrap: true })
        {
            return true;
        }

        return col >= 0 && col < Fields.Count
            && Fields[col] is IGriddoFieldWrapView { NoWrap: true };
    }

    private static string NormalizeStyleText(string style)
    {
        if (string.IsNullOrWhiteSpace(style))
        {
            return string.Empty;
        }

        return style
            .Trim()
            .Replace("-", " ", StringComparison.Ordinal)
            .Replace("_", " ", StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private void DrawHeaders(DrawingContext dc)
    {
        if (IsBodyTransposed)
        {
            DrawHeadersTransposed(dc);
            return;
        }

        // Fixed top-left corner header cell (record-header field title area). Outer top/left strokes live in DrawOuterWorksheetFrame only.
        var cornerRect = new Rect(0, 0, _recordHeaderWidth, ScaledFieldHeaderHeight);
        dc.DrawRectangle(HeaderBackground, null, cornerRect);

        var typeface = new Typeface("Segoe UI");
        var fixedW = GetFixedFieldsWidth();
        var scrollLeft = _recordHeaderWidth + fixedW;

        if (_fixedFieldCount < Fields.Count && scrollLeft < _recordHeaderWidth + _viewportBodyWidth)
        {
            var scrollClipW = Math.Max(0, _viewportBodyWidth - fixedW);
            var scrollClip = new Rect(scrollLeft, 0, scrollClipW, ScaledFieldHeaderHeight);
            dc.PushClip(new RectangleGeometry(scrollClip));
            GetVisibleScrollFieldRange(out var sCol, out var eCol, out var x);
            if (eCol >= sCol)
            {
                for (var col = sCol; col <= eCol; col++)
                {
                    DrawFieldHeader(dc, col, x, typeface, scrollClip);
                    x += GetFieldWidth(col);
                }
            }

            dc.Pop();
        }

        if (_fixedFieldCount > 0)
        {
            var fixedClipW = Math.Min(fixedW, _viewportBodyWidth);
            var fixedClip = new Rect(_recordHeaderWidth, 0, fixedClipW, ScaledFieldHeaderHeight);
            dc.PushClip(new RectangleGeometry(fixedClip));
            var fx = _recordHeaderWidth;
            for (var col = 0; col < _fixedFieldCount; col++)
            {
                DrawFieldHeader(dc, col, fx, typeface, fixedClip);
                fx += GetFieldWidth(col);
            }

            dc.Pop();
        }

        DrawFieldMoveCue(dc);

        var recordHeaderClip = new Rect(0, ScaledFieldHeaderHeight, _recordHeaderWidth, _viewportBodyHeight);
        dc.PushClip(new RectangleGeometry(recordHeaderClip));
        {
            var recordHeaderPen = ResolveGridLinePen();
            ForEachVisibleRecord(record =>
            {
                var recordHeight = GetRecordHeight(record);
                var y = ScaledFieldHeaderHeight + GetRecordBodyTopRel(record);
                var rect = new Rect(0, y, _recordHeaderWidth, recordHeight);
                var isSelectedRecordHeader = ShouldShowSelectionVisuals() && IsRecordHeaderMarkedSelected(record) && ShowRecordHeaderSelectionColoring;
                var recordHeaderBackground = isSelectedRecordHeader ? HeaderSelectionBackground : HeaderBackground;
                dc.DrawRectangle(recordHeaderBackground, null, rect);
                // Top + bottom only; outer x=0 edge is one DrawLine in DrawOuterWorksheetFrame (avoids path vs line mismatch).
                dc.DrawLine(recordHeaderPen, new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top));
                dc.DrawLine(recordHeaderPen, new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Bottom));
                var visibleRect = Rect.Intersect(rect, recordHeaderClip);
                if (!visibleRect.IsEmpty)
                {
                    GriddoValuePainter.Paint(
                        dc,
                        record + 1,
                        visibleRect,
                        typeface,
                        EffectiveFontSize,
                        ResolveHeaderForeground(isSelectedRecordHeader),
                        underline: false,
                        treatAsHtml: false,
                        autoDetectHtml: false,
                        alignment: TextAlignment.Right,
                        verticalAlignment: VerticalAlignment.Center);
                }
            });
        }
        dc.Pop();

        DrawHeaderFocusHighlight(dc);
        DrawOuterWorksheetFrame(dc);
    }

    private void DrawHeadersTransposed(DrawingContext dc)
    {
        var cornerRect = new Rect(0, 0, _recordHeaderWidth, ScaledFieldHeaderHeight);
        dc.DrawRectangle(HeaderBackground, null, cornerRect);
        var typeface = new Typeface("Segoe UI");
        var bodyRecordHeaderLeft = _recordHeaderWidth;
        var headerStripH = ScaledFieldHeaderHeight;
        var fixedRecordsW = GetTransposeFixedRecordsWidth();
        var fRecords = GetEffectiveFixedRecordCount();
        var fixedRecordsHeaderW = Math.Min(fixedRecordsW, _viewportBodyWidth);
        var scrollRecordsHeaderW = Math.Max(0, _viewportBodyWidth - fixedRecordsW);

        void DrawRecordHeaderStripCell(int record, Rect clipIntersect)
        {
            var rr = GetRecordHeaderRect(record);
            var isSelectedRecordHeader = ShouldShowSelectionVisuals() && IsRecordHeaderMarkedSelected(record) && ShowRecordHeaderSelectionColoring;
            var recordHeaderBackground = isSelectedRecordHeader ? HeaderSelectionBackground : HeaderBackground;
            dc.DrawRectangle(recordHeaderBackground, null, rr);
            var pen = ResolveGridLinePen();
            dc.DrawLine(pen, rr.TopLeft, rr.BottomLeft);
            dc.DrawLine(pen, rr.TopRight, rr.BottomRight);
            var visibleRect = Rect.Intersect(rr, clipIntersect);
            if (!visibleRect.IsEmpty)
            {
                GriddoValuePainter.Paint(
                    dc,
                    record + 1,
                    visibleRect,
                    typeface,
                    EffectiveFontSize,
                    ResolveHeaderForeground(isSelectedRecordHeader),
                    underline: false,
                    treatAsHtml: false,
                    autoDetectHtml: false,
                    alignment: TextAlignment.Center,
                    verticalAlignment: VerticalAlignment.Center);
            }
        }

        var clipTopFull = new Rect(bodyRecordHeaderLeft, 0, _viewportBodyWidth, headerStripH);

        // Left: frozen record headers — horizontal scroll must not bleed into this strip from scroll records.
        if (fixedRecordsHeaderW > 0 && fRecords > 0)
        {
            var clipFixedTop = new Rect(bodyRecordHeaderLeft, 0, fixedRecordsHeaderW, headerStripH);
            dc.PushClip(new RectangleGeometry(clipFixedTop));
            for (var record = 0; record < fRecords && record < Records.Count; record++)
            {
                DrawRecordHeaderStripCell(record, clipTopFull);
            }

            dc.Pop();
        }

        // Right: scroll record headers only
        if (scrollRecordsHeaderW > 0)
        {
            var clipScrollTop = new Rect(bodyRecordHeaderLeft + fixedRecordsW, 0, scrollRecordsHeaderW, headerStripH);
            dc.PushClip(new RectangleGeometry(clipScrollTop));
            ForEachVisibleScrollRecordForTranspose(record =>
            {
                if (record < fRecords)
                {
                    return;
                }

                DrawRecordHeaderStripCell(record, clipTopFull);
            });
            dc.Pop();
        }

        var bodyTop = ScaledFieldHeaderHeight;
        var fixedColsH = GetFixedFieldsWidth();
        var fCols = Math.Clamp(_fixedFieldCount, 0, Fields.Count);
        var bodyH = _viewportBodyHeight;
        var fixedColsClipH = Math.Min(fixedColsH, bodyH);
        var scrollColHeadersH = Math.Max(0, bodyH - fixedColsH);

        void DrawFieldHeaderCell(int col, Rect clipRect)
        {
            var rr = GetFieldHeaderRect(col);
            var isSelectedHeader = ShouldShowSelectionVisuals() && IsFieldHeaderMarkedSelected(col) && ShowFieldHeaderSelectionColoring;
            var headerBackground = isSelectedHeader ? HeaderSelectionBackground : HeaderBackground;
            dc.DrawRectangle(headerBackground, null, rr);
            var pen = ResolveGridLinePen();
            dc.DrawLine(pen, rr.TopLeft, rr.TopRight);
            dc.DrawLine(pen, rr.BottomLeft, rr.BottomRight);
            var headerLabel = Fields[col] is IGriddoFieldTitleView titleView && !string.IsNullOrWhiteSpace(titleView.AbbreviatedHeader)
                ? titleView.AbbreviatedHeader
                : Fields[col].Header;
            var headerText = new FormattedText(
                headerLabel,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                EffectiveFontSize,
                ResolveHeaderForeground(isSelectedHeader),
                1.0);
            headerText.SetFontWeight(ResolveFieldHeaderFontWeight(col));
            headerText.TextAlignment = TextAlignment.Center;
            var visibleRect = Rect.Intersect(rr, clipRect);
            if (visibleRect.IsEmpty)
            {
                return;
            }

            headerText.MaxTextWidth = Math.Max(1, visibleRect.Width - 8);
            headerText.MaxTextHeight = Math.Max(1, visibleRect.Height - 8);
            headerText.Trimming = TextTrimming.CharacterEllipsis;
            var tx = visibleRect.X + 4;
            var ty = visibleRect.Y + Math.Max(0, (visibleRect.Height - headerText.Height) / 2);
            dc.DrawText(headerText, new Point(tx, ty));
            DrawSortHeaderIndicator(dc, col, rr, typeface);
        }

        // Clip frozen vs scroll field headers separately so vertical scroll does not bleed into frozen bands.
        if (fixedColsClipH > 0 && fCols > 0)
        {
            var clipFixed = new Rect(0, bodyTop, _recordHeaderWidth, fixedColsClipH);
            dc.PushClip(new RectangleGeometry(clipFixed));
            for (var col = 0; col < fCols && col < Fields.Count; col++)
            {
                DrawFieldHeaderCell(col, clipFixed);
            }

            dc.Pop();
        }

        if (scrollColHeadersH > 0 && fCols < Fields.Count)
        {
            var clipScroll = new Rect(0, bodyTop + fixedColsH, _recordHeaderWidth, scrollColHeadersH);
            dc.PushClip(new RectangleGeometry(clipScroll));
            ForEachVisibleFieldForTranspose(col =>
            {
                if (col < fCols)
                {
                    return;
                }

                DrawFieldHeaderCell(col, clipScroll);
            });
            dc.Pop();
        }

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
        var fieldHeaderClip = new Rect(_recordHeaderWidth, 0, _viewportBodyWidth, ScaledFieldHeaderHeight);
        var recordHeaderClip = new Rect(0, ScaledFieldHeaderHeight, _recordHeaderWidth, _viewportBodyHeight);
        var cornerHeaderClip = new Rect(0, 0, _recordHeaderWidth, ScaledFieldHeaderHeight);

        void DrawOutlinedRect(Rect rect, Rect clipRect)
        {
            if (rect.IsEmpty)
            {
                return;
            }

            rect = Rect.Intersect(rect, clipRect);
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

        if (_headerFocusKind == HeaderFocusKind.Field && _fieldHeaderRightClickOutline.Count > 0)
        {
            foreach (var col in _fieldHeaderRightClickOutline.OrderBy(c => c))
            {
                DrawOutlinedRect(GetFieldHeaderRect(col), fieldHeaderClip);
            }

            return;
        }

        if (_headerFocusKind == HeaderFocusKind.Record && _recordHeaderRightClickOutline.Count > 0)
        {
            foreach (var record in _recordHeaderRightClickOutline.OrderBy(r => r))
            {
                DrawOutlinedRect(GetRecordHeaderRect(record), recordHeaderClip);
            }

            return;
        }

        Rect r = Rect.Empty;
        if (_headerFocusKind == HeaderFocusKind.Corner)
        {
            r = new Rect(0, 0, _recordHeaderWidth, ScaledFieldHeaderHeight);
        }
        else if (_headerFocusKind == HeaderFocusKind.Field
                 && _headerFocusFieldIndex >= 0
                 && _headerFocusFieldIndex < Fields.Count)
        {
            r = GetFieldHeaderRect(_headerFocusFieldIndex);
        }
        else if (_headerFocusKind == HeaderFocusKind.Record
                 && _headerFocusRecordIndex >= 0
                 && _headerFocusRecordIndex < Records.Count)
        {
            r = GetRecordHeaderRect(_headerFocusRecordIndex);
        }

        var clip = _headerFocusKind switch
        {
            HeaderFocusKind.Field => fieldHeaderClip,
            HeaderFocusKind.Record => recordHeaderClip,
            HeaderFocusKind.Corner => cornerHeaderClip,
            _ => Rect.Empty
        };
        DrawOutlinedRect(r, clip);
    }

    /// <summary>
    /// Single DrawLine for the outermost grid edges so they match field header strokes (PathGeometry stroke looked thicker under AA).
    /// </summary>
    private void DrawOuterWorksheetFrame(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var pen = ResolveGridLinePen();
        var topRight = Math.Max(0, ActualWidth - EffectiveVerticalScrollBarThickness);
        var stripBottom = ScaledFieldHeaderHeight + Math.Max(0, _viewportBodyHeight);
        var layoutBottom = Math.Max(0, ActualHeight - EffectiveHorizontalScrollBarThickness);
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
        if (Records.Count == 0)
        {
            return;
        }

        if (IsBodyTransposed)
        {
            DrawBodyTransposed(dc);
            return;
        }

        var bodyViewport = new Rect(_recordHeaderWidth, ScaledFieldHeaderHeight, _viewportBodyWidth, _viewportBodyHeight);
        var typeface = new Typeface("Segoe UI");
        var recordHeight = GetRecordHeight(0);
        var fixedW = GetFixedFieldsWidth();
        var scrollLeft = _recordHeaderWidth + fixedW;

        if (_fixedFieldCount < Fields.Count && scrollLeft < _recordHeaderWidth + _viewportBodyWidth)
        {
            var scrollClipW = Math.Max(0, _viewportBodyWidth - fixedW);
            var scrollClip = new Rect(scrollLeft, ScaledFieldHeaderHeight, scrollClipW, _viewportBodyHeight);
            dc.PushClip(new RectangleGeometry(scrollClip));
            GetVisibleScrollFieldRange(out var sCol, out var eCol, out var startX);
            if (eCol >= sCol)
            {
                ForEachVisibleRecord(record =>
                {
                    var y = ScaledFieldHeaderHeight + GetRecordBodyTopRel(record);
                    var recordData = Records[record];
                    var x = startX;
                    for (var col = sCol; col <= eCol; col++)
                    {
                        DrawBodyCell(dc, record, col, x, y, recordHeight, recordData, bodyViewport, typeface);
                        x += GetFieldWidth(col);
                    }
                });
            }

            dc.Pop();
        }

        if (_fixedFieldCount > 0)
        {
            var fixedClipW = Math.Min(fixedW, _viewportBodyWidth);
            var fixedClip = new Rect(_recordHeaderWidth, ScaledFieldHeaderHeight, fixedClipW, _viewportBodyHeight);
            dc.PushClip(new RectangleGeometry(fixedClip));
            ForEachVisibleRecord(record =>
            {
                var y = ScaledFieldHeaderHeight + GetRecordBodyTopRel(record);
                var recordData = Records[record];
                var x = _recordHeaderWidth;
                for (var col = 0; col < _fixedFieldCount; col++)
                {
                    DrawBodyCell(dc, record, col, x, y, recordHeight, recordData, bodyViewport, typeface);
                    x += GetFieldWidth(col);
                }
            });

            dc.Pop();
        }

        DrawFixedRecordSeparator(dc);
    }

    private void DrawBodyTransposed(DrawingContext dc)
    {
        var bodyViewport = new Rect(_recordHeaderWidth, ScaledFieldHeaderHeight, _viewportBodyWidth, _viewportBodyHeight);
        var typeface = new Typeface("Segoe UI");
        var bodyLeft = _recordHeaderWidth;
        var bodyTop = ScaledFieldHeaderHeight;
        var bodyW = _viewportBodyWidth;
        var bodyH = _viewportBodyHeight;
        var fixedRecordsW = GetTransposeFixedRecordsWidth();
        var fixedColsH = GetFixedFieldsWidth();
        var fRecords = GetEffectiveFixedRecordCount();
        var fCols = Math.Clamp(_fixedFieldCount, 0, Fields.Count);
        var scrollLeft = bodyLeft + fixedRecordsW;
        var scrollTop = bodyTop + fixedColsH;
        var scrollW = Math.Max(0, bodyW - fixedRecordsW);
        var scrollH = Math.Max(0, bodyH - fixedColsH);
        var fixedRecordsClipW = Math.Min(fixedRecordsW, bodyW);
        var fixedColsClipH = Math.Min(fixedColsH, bodyH);

        void DrawCell(int record, int col)
        {
            var rect = GetCellRect(record, col);
            if (rect.IsEmpty)
            {
                return;
            }

            DrawBodyCell(dc, record, col, rect.X, rect.Y, GetRecordHeight(record), Records[record], bodyViewport, typeface);
        }

        // Split body into four clips so vertically scrolled field bands cannot paint over frozen top fields.
        // Order: scroll×scroll (back), then top-right & bottom-left, then frozen×frozen (front).

        // Q_ss — scroll records × scroll fields (below frozen fields, right of frozen records)
        if (scrollW > 0 && scrollH > 0)
        {
            dc.PushClip(new RectangleGeometry(new Rect(scrollLeft, scrollTop, scrollW, scrollH)));
            ForEachVisibleFieldForTranspose(col =>
            {
                if (col < fCols)
                {
                    return;
                }

                ForEachVisibleScrollRecordForTranspose(record => DrawCell(record, col));
            });
            dc.Pop();
        }

        // Q_sf — scroll records × frozen fields (top strip on the right)
        if (scrollW > 0 && fixedColsClipH > 0 && fCols > 0)
        {
            dc.PushClip(new RectangleGeometry(new Rect(scrollLeft, bodyTop, scrollW, fixedColsClipH)));
            ForEachVisibleFieldForTranspose(col =>
            {
                if (col >= fCols)
                {
                    return;
                }

                ForEachVisibleScrollRecordForTranspose(record => DrawCell(record, col));
            });
            dc.Pop();
        }

        // Q_fs — frozen records × scroll fields (left strip below frozen fields)
        if (fixedRecordsClipW > 0 && scrollH > 0 && fRecords > 0)
        {
            dc.PushClip(new RectangleGeometry(new Rect(bodyLeft, scrollTop, fixedRecordsClipW, scrollH)));
            ForEachVisibleFieldForTranspose(col =>
            {
                if (col < fCols)
                {
                    return;
                }

                for (var record = 0; record < fRecords && record < Records.Count; record++)
                {
                    DrawCell(record, col);
                }
            });
            dc.Pop();
        }

        // Q_ff — frozen records × frozen fields (top-left corner)
        if (fixedRecordsClipW > 0 && fixedColsClipH > 0 && fRecords > 0 && fCols > 0)
        {
            dc.PushClip(new RectangleGeometry(new Rect(bodyLeft, bodyTop, fixedRecordsClipW, fixedColsClipH)));
            for (var col = 0; col < fCols && col < Fields.Count; col++)
            {
                for (var record = 0; record < fRecords && record < Records.Count; record++)
                {
                    DrawCell(record, col);
                }
            }

            dc.Pop();
        }

        DrawFixedRecordSeparator(dc);
    }

    private void DrawFixedRecordSeparator(DrawingContext dc)
    {
        if (IsBodyTransposed)
        {
            var w = GetTransposeFixedRecordsWidth();
            if (w <= 1e-6)
            {
                return;
            }

            var xLine = _recordHeaderWidth + w;
            if (xLine > _recordHeaderWidth + _viewportBodyWidth)
            {
                return;
            }

            var transposePen = ResolveFixedFieldRightPen();
            dc.DrawLine(
                transposePen,
                new Point(xLine, ScaledFieldHeaderHeight),
                new Point(xLine, ScaledFieldHeaderHeight + _viewportBodyHeight));
            return;
        }

        if (GetScrollableRecordsContentHeight() <= 1e-6)
        {
            return;
        }

        var f = GetEffectiveFixedRecordCount();
        if (f <= 0)
        {
            return;
        }

        var h = GetRecordHeight(0);
        var yLine = ScaledFieldHeaderHeight + f * h;
        if (yLine > ScaledFieldHeaderHeight + _viewportBodyHeight)
        {
            return;
        }

        var normalPen = ResolveFixedRecordBottomPen();
        dc.DrawLine(normalPen, new Point(_recordHeaderWidth, yLine), new Point(_recordHeaderWidth + _viewportBodyWidth, yLine));
    }
    private void DrawCurrentCellOverlay(DrawingContext dc)
    {
        if (!_currentCell.IsValid)
        {
            return;
        }

        if (!ShouldShowSelectionVisuals())
        {
            return;
        }

        var baseRect = GetCellRect(_currentCell.RecordIndex, _currentCell.FieldIndex);
        var rect = GetMergedRecordCellRect(_currentCell.RecordIndex, _currentCell.FieldIndex, baseRect);
        if (rect.IsEmpty)
        {
            return;
        }

        var bodyViewport = new Rect(_recordHeaderWidth, ScaledFieldHeaderHeight, _viewportBodyWidth, _viewportBodyHeight);
        if (IsBodyTransposed)
        {
            dc.PushClip(new RectangleGeometry(Rect.Intersect(rect, bodyViewport)));
        }
        else
        {
            dc.PushClip(new RectangleGeometry(GetFieldBodyBandClipRect(_currentCell.FieldIndex)));
        }

        const double currentCellInset = 0.5;
        var insetRect = new Rect(
            rect.X + currentCellInset,
            rect.Y + currentCellInset,
            Math.Max(0, rect.Width - (currentCellInset * 2)),
            Math.Max(0, rect.Height - (currentCellInset * 2)));
        var isHostedEditMode = IsCurrentHostedCellInEditMode();
        var isEditCell = _isEditing || isHostedEditMode;
        if ((isEditCell && !ShowEditCellColor) || (!isEditCell && !ShowCurrentCellColor))
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

        var baseRect = GetCellRect(_currentCell.RecordIndex, _currentCell.FieldIndex);
        var rect = GetMergedRecordCellRect(_currentCell.RecordIndex, _currentCell.FieldIndex, baseRect);
        if (rect.IsEmpty)
        {
            return;
        }

        if (!TryGetCurrentField(out var field))
        {
            return;
        }

        var bodyViewportForEdit = new Rect(_recordHeaderWidth, ScaledFieldHeaderHeight, _viewportBodyWidth, _viewportBodyHeight);
        if (IsBodyTransposed)
        {
            dc.PushClip(new RectangleGeometry(Rect.Intersect(rect, bodyViewportForEdit)));
        }
        else
        {
            dc.PushClip(new RectangleGeometry(GetFieldBodyBandClipRect(_currentCell.FieldIndex)));
        }

        // Keep editor visuals inside the cell border so the edit outline thickness stays consistent.
        const double editContentInset = 1.0;
        var fullEditRect = new Rect(
            rect.X + editContentInset,
            rect.Y + editContentInset,
            Math.Max(0, rect.Width - (editContentInset * 2)),
            Math.Max(0, rect.Height - (editContentInset * 2)));
        var editContentRect = GetInlineEditTextRect(fullEditRect);

        if (field.IsHtml)
        {
            var bodyCellsViewport = new Rect(_recordHeaderWidth, ScaledFieldHeaderHeight, _viewportBodyWidth, _viewportBodyHeight);
            editContentRect = Rect.Intersect(editContentRect, bodyCellsViewport);
        }

        if (editContentRect.IsEmpty)
        {
            dc.Pop();
            return;
        }

        var typeface = new Typeface("Segoe UI");
        var fontSize = EffectiveFontSize;
        dc.DrawRectangle(BodyBackground, null, fullEditRect);
        if (TryGetInlineDialogButtonRect(fullEditRect, out var dialogButtonRect))
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(240, 240, 240)), new Pen(Brushes.Gray, 1), dialogButtonRect);
            var buttonText = new FormattedText(
                "...",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                Math.Max(9, fontSize * 0.9),
                BodyForeground,
                1.0);
            var buttonTextX = dialogButtonRect.X + Math.Max(0, (dialogButtonRect.Width - buttonText.Width) / 2);
            var buttonTextY = dialogButtonRect.Y + Math.Max(0, (dialogButtonRect.Height - buttonText.Height) / 2);
            dc.DrawText(buttonText, new Point(buttonTextX, buttonTextY));
        }
        var verticalAlignment = VerticalAlignment.Center;
        var underline = _currentCell.IsValid && HasUnderlineStyle(_currentCell.FieldIndex, null);
        // Edit mode should show the literal source text (including HTML markup), not rendered HTML.
        GriddoValuePainter.Paint(dc, _editSession.Buffer, editContentRect, typeface, fontSize, BodyForeground, underline, false, false, field.ContentAlignment, verticalAlignment);

        var displayText = _editSession.Buffer;
        var editText = new FormattedText(
            displayText,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            BodyForeground,
            1.0);
        editText.TextAlignment = field.ContentAlignment;
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
            BodyForeground,
            1.0);
        var contentWidth = Math.Max(1, editContentRect.Width - 8);
        var totalTextWidth = Math.Min(editText.WidthIncludingTrailingWhitespace, contentWidth);
        var textStartX = editContentRect.X + 4;
        if (field.ContentAlignment == TextAlignment.Right)
        {
            textStartX += Math.Max(0, contentWidth - totalTextWidth);
        }
        else if (field.ContentAlignment == TextAlignment.Center)
        {
            textStartX += Math.Max(0, (contentWidth - totalTextWidth) / 2);
        }

        if (_editSession.TryGetSelection(out var selectionStart, out var selectionEnd))
        {
            if (field.IsHtml)
            {
                var selectionGeometry = editText.BuildHighlightGeometry(new Point(textStartX, caretOriginY), selectionStart, selectionEnd - selectionStart);
                if (selectionGeometry is not null && !selectionGeometry.Bounds.IsEmpty)
                {
                    dc.PushClip(new RectangleGeometry(editContentRect));
                    dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(120, 102, 178, 255)), null, selectionGeometry);
                    GriddoValuePainter.Paint(dc, _editSession.Buffer, editContentRect, typeface, fontSize, BodyForeground, underline, false, false, field.ContentAlignment, verticalAlignment);
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
                    BodyForeground,
                    1.0).WidthIncludingTrailingWhitespace;
                var selectedWidth = new FormattedText(
                    selectedText,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    BodyForeground,
                    1.0).WidthIncludingTrailingWhitespace;
                var selectionX = Math.Clamp(textStartX + Math.Min(beforeWidth, contentWidth), editContentRect.X + 2, editContentRect.Right - 2);
                var selectionRight = Math.Clamp(selectionX + Math.Min(selectedWidth, contentWidth), editContentRect.X + 2, editContentRect.Right - 2);
                if (selectionRight > selectionX)
                {
                    var selectionRect = new Rect(selectionX, caretOriginY, selectionRight - selectionX, Math.Max(1, editText.Height));
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(120, 102, 178, 255)), null, selectionRect);
                    GriddoValuePainter.Paint(dc, _editSession.Buffer, editContentRect, typeface, fontSize, BodyForeground, underline, false, false, field.ContentAlignment, verticalAlignment);
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
            dc.DrawLine(new Pen(BodyForeground, 1), new Point(caretX, caretTop), new Point(caretX, caretBottom));
        }

        dc.Pop();
    }

    private Rect GetMergedRecordCellRect(int record, int col, Rect fallbackRect)
    {
        if (IsBodyTransposed
            || col < 0
            || col >= Fields.Count
            || record < 0
            || record >= Records.Count
            || Fields[col] is not IGriddoRecordMergeBandView mergeBand)
        {
            return fallbackRect;
        }

        var topRecord = record;
        while (topRecord > 0 && mergeBand.IsMergedWithPreviousRecord(Records, topRecord))
        {
            topRecord--;
        }

        var bottomRecord = record;
        while (bottomRecord < Records.Count - 1 && mergeBand.IsMergedWithNextRecord(Records, bottomRecord))
        {
            bottomRecord++;
        }

        if (topRecord == record && bottomRecord == record)
        {
            return fallbackRect;
        }

        var topRect = GetCellRect(topRecord, col);
        var bottomRect = GetCellRect(bottomRecord, col);
        if (topRect.IsEmpty || bottomRect.IsEmpty)
        {
            return fallbackRect;
        }

        return new Rect(
            fallbackRect.X,
            topRect.Y,
            fallbackRect.Width,
            Math.Max(0, bottomRect.Bottom - topRect.Y));
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
    private void DrawFieldMoveCue(DrawingContext dc)
    {
        if (!_isTrackingFieldMove)
        {
            return;
        }

        var clipRect = new Rect(_recordHeaderWidth, 0, _viewportBodyWidth, ScaledFieldHeaderHeight);

        // Keep a thin red "current/source" marker on the field(s) being moved.
        if (_isMovingPointerInFieldHeader && _movingFieldIndex >= 0 && _movingFieldIndex < Fields.Count)
        {
            var currentPen = new Pen(Brushes.Red, 1);
            var movingFields = _fieldMoveStartedFromSelectedHeader && _isMovingField
                ? GetSelectedFieldIndices()
                : [_movingFieldIndex];
            foreach (var movingField in movingFields)
            {
                if (movingField < 0 || movingField >= Fields.Count)
                {
                    continue;
                }

                var movingRect = GetFieldHeaderRect(movingField);
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

        if (_fieldMoveCueIndex < 0 || _fieldMoveCueIndex >= Fields.Count)
        {
            return;
        }

        var cueRect = GetFieldHeaderRect(_fieldMoveCueIndex);
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
        if (_fieldMoveStartedFromSelectedHeader && _isMovingField)
        {
            var selectedFields = GetSelectedFieldIndices();
            if (selectedFields.Count > 0)
            {
                var minSelected = selectedFields[0];
                var maxSelected = selectedFields[^1];
                var movingLeft = _fieldMoveCueIndex < minSelected;
                var movingRight = _fieldMoveCueIndex > maxSelected;
                x = movingRight ? visibleCueRect.Right : visibleCueRect.Left;
            }
        }
        else
        {
            var movingRight = _movingFieldIndex >= 0 && _fieldMoveCueIndex > _movingFieldIndex;
            x = movingRight ? visibleCueRect.Right : visibleCueRect.Left;
        }
        var insertionPen = new Pen(Brushes.Red, 2);
        dc.DrawLine(
            insertionPen,
            new Point(x, 1),
            new Point(x, Math.Max(1, ScaledFieldHeaderHeight - 1)));

        DrawDropArrows(dc, x, ScaledFieldHeaderHeight);
    }

    private void DrawRecordMoveCue(DrawingContext dc)
    {
        if (!_isTrackingRecordMove || !_isMovingRecord)
        {
            return;
        }

        var clipRect = new Rect(0, ScaledFieldHeaderHeight, _recordHeaderWidth, _viewportBodyHeight);
        var currentPen = new Pen(Brushes.Red, 1);
        var movingRecords = GetSelectedRecordIndices();
        foreach (var movingRecord in movingRecords)
        {
            var movingRect = GetRecordHeaderRect(movingRecord);
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

        if (_recordMoveCueIndex < 0 || _recordMoveCueIndex >= Records.Count)
        {
            return;
        }

        var cueRect = GetRecordHeaderRect(_recordMoveCueIndex);
        if (cueRect.IsEmpty)
        {
            return;
        }

        if (!TryGetRecordDropIndicatorY(out var y))
        {
            return;
        }

        y = Math.Clamp(y, clipRect.Top + 1, clipRect.Bottom - 1);
        var insertionPen = new Pen(Brushes.Red, 2);
        dc.DrawLine(
            insertionPen,
            new Point(1, y),
            new Point(Math.Max(1, _recordHeaderWidth - 1), y));

        DrawRecordDropArrows(dc, y, _recordHeaderWidth, clipRect.Top + 1, clipRect.Bottom - 1);
    }

    private bool TryGetRecordDropIndicatorY(out double y)
    {
        y = 0;
        if (_recordMoveCueIndex < 0 || _recordMoveCueIndex >= Records.Count)
        {
            return false;
        }

        var selectedRecords = GetSelectedRecordIndices();
        if (selectedRecords.Count == 0)
        {
            if (_movingRecordIndex < 0 || _movingRecordIndex >= Records.Count || _recordMoveCueIndex == _movingRecordIndex)
            {
                return false;
            }

            var cueRectSingle = GetRecordHeaderRect(_recordMoveCueIndex);
            if (cueRectSingle.IsEmpty)
            {
                return false;
            }

            y = _recordMoveCueIndex > _movingRecordIndex ? cueRectSingle.Bottom : cueRectSingle.Top;
            return true;
        }

        var minSelected = selectedRecords[0];
        var maxSelected = selectedRecords[^1];
        if (_recordMoveCueIndex >= minSelected && _recordMoveCueIndex <= maxSelected)
        {
            return false;
        }

        var cueRect = GetRecordHeaderRect(_recordMoveCueIndex);
        if (cueRect.IsEmpty)
        {
            return false;
        }

        var insertAfterTarget = _recordMoveCueIndex > maxSelected;
        y = insertAfterTarget ? cueRect.Bottom : cueRect.Top;
        return true;
    }

    private static void DrawDropArrows(DrawingContext dc, double lineX, double headerHeight)
    {
        const double arrecordWidth = 6;
        const double arrecordHeight = 4;
        const double gap = 3;
        var centerY = headerHeight / 2.0;

        var red = Brushes.Red;

        // Left arrecord pointing right.
        var leftArrow = new StreamGeometry();
        using (var ctx = leftArrow.Open())
        {
            var tip = new Point(lineX - gap, centerY);
            ctx.BeginFigure(new Point(tip.X - arrecordWidth, tip.Y - arrecordHeight), true, true);
            ctx.LineTo(new Point(tip.X - arrecordWidth, tip.Y + arrecordHeight), true, false);
            ctx.LineTo(tip, true, false);
        }
        leftArrow.Freeze();
        dc.DrawGeometry(red, null, leftArrow);

        // Right arrecord pointing left.
        var rightArrow = new StreamGeometry();
        using (var ctx = rightArrow.Open())
        {
            var tip = new Point(lineX + gap, centerY);
            ctx.BeginFigure(new Point(tip.X + arrecordWidth, tip.Y - arrecordHeight), true, true);
            ctx.LineTo(new Point(tip.X + arrecordWidth, tip.Y + arrecordHeight), true, false);
            ctx.LineTo(tip, true, false);
        }
        rightArrow.Freeze();
        dc.DrawGeometry(red, null, rightArrow);
    }

    private static void DrawRecordDropArrows(DrawingContext dc, double lineY, double headerWidth, double minY, double maxY)
    {
        const double arrecordWidth = 4;
        const double arrecordHeight = 6;
        const double gap = 3;
        var clampedLineY = Math.Clamp(lineY, minY, maxY);
        var centerX = headerWidth / 2.0;
        var red = Brushes.Red;

        // Down arrecord above insertion line.
        var downArrow = new StreamGeometry();
        using (var ctx = downArrow.Open())
        {
            var tipY = Math.Clamp(clampedLineY - gap, minY + arrecordHeight, maxY - arrecordHeight);
            var tip = new Point(centerX, tipY);
            ctx.BeginFigure(new Point(tip.X - arrecordWidth, tip.Y - arrecordHeight), true, true);
            ctx.LineTo(new Point(tip.X + arrecordWidth, tip.Y - arrecordHeight), true, false);
            ctx.LineTo(tip, true, false);
        }
        downArrow.Freeze();
        dc.DrawGeometry(red, null, downArrow);

        // Up arrecord below insertion line.
        var upArrow = new StreamGeometry();
        using (var ctx = upArrow.Open())
        {
            var tipY = Math.Clamp(clampedLineY + gap, minY + arrecordHeight, maxY - arrecordHeight);
            var tip = new Point(centerX, tipY);
            ctx.BeginFigure(new Point(tip.X - arrecordWidth, tip.Y + arrecordHeight), true, true);
            ctx.LineTo(new Point(tip.X + arrecordWidth, tip.Y + arrecordHeight), true, false);
            ctx.LineTo(tip, true, false);
        }
        upArrow.Freeze();
        dc.DrawGeometry(red, null, upArrow);
    }

    private Rect GetFieldHeaderRect(int fieldIndex)
    {
        if (fieldIndex < 0 || fieldIndex >= Fields.Count)
        {
            return Rect.Empty;
        }

        if (IsBodyTransposed)
        {
            var y = ScaledFieldHeaderHeight + GetTransposedFieldBodyTopRel(fieldIndex);
            return new Rect(0, y, _recordHeaderWidth, GetFieldWidth(fieldIndex));
        }

        double left;
        if (fieldIndex < _fixedFieldCount)
        {
            left = _recordHeaderWidth;
            for (var col = 0; col < fieldIndex; col++)
            {
                left += GetFieldWidth(col);
            }
        }
        else
        {
            left = _recordHeaderWidth + GetFixedFieldsWidth();
            for (var col = _fixedFieldCount; col < fieldIndex; col++)
            {
                left += GetFieldWidth(col);
            }

            left -= _horizontalOffset;
        }

        return new Rect(left, 0, GetFieldWidth(fieldIndex), ScaledFieldHeaderHeight);
    }

    private Rect GetRecordHeaderRect(int recordIndex)
    {
        if (recordIndex < 0 || recordIndex >= Records.Count)
        {
            return Rect.Empty;
        }

        if (IsBodyTransposed)
        {
            var x = _recordHeaderWidth + GetTransposedRecordBodyLeftRel(recordIndex);
            return new Rect(x, 0, GetRecordHeight(recordIndex), ScaledFieldHeaderHeight);
        }

        var y = ScaledFieldHeaderHeight + GetRecordBodyTopRel(recordIndex);
        return new Rect(0, y, _recordHeaderWidth, GetRecordHeight(recordIndex));
    }
    private void DrawScrollBarCorner(DrawingContext dc)
    {
        if (!ShowHorizontalScrollBar && !ShowVerticalScrollBar)
        {
            return;
        }

        const double outerBorderInset = 1;
        var horizontalThickness = ShowHorizontalScrollBar ? ScrollBarSize : 0;
        var verticalThickness = ShowVerticalScrollBar ? ScrollBarSize : 0;
        var cornerThicknessX = Math.Max(0, verticalThickness - outerBorderInset);
        var cornerThicknessY = Math.Max(0, horizontalThickness - outerBorderInset);
        var topRightRect = new Rect(
            Math.Max(0, ActualWidth - verticalThickness - outerBorderInset),
            0,
            cornerThicknessX,
            Math.Max(0, ScaledFieldHeaderHeight));
        var bottomLeftRect = new Rect(
            0,
            Math.Max(0, ActualHeight - horizontalThickness - outerBorderInset),
            Math.Max(0, _recordHeaderWidth),
            cornerThicknessY);
        var bottomRightRect = new Rect(
            Math.Max(0, ActualWidth - verticalThickness - outerBorderInset),
            Math.Max(0, ActualHeight - horizontalThickness - outerBorderInset),
            cornerThicknessX,
            cornerThicknessY);

        if (ShowVerticalScrollBar)
        {
            dc.DrawRectangle(HeaderBackground, null, topRightRect);
        }

        if (ShowHorizontalScrollBar)
        {
            dc.DrawRectangle(HeaderBackground, null, bottomLeftRect);
        }

        if (ShowVerticalScrollBar && ShowHorizontalScrollBar)
        {
            dc.DrawRectangle(HeaderBackground, null, bottomRightRect);
        }

        var pen = ResolveGridLinePen();

        if (ShowVerticalScrollBar)
        {
            // Top-right: only top border.
            dc.DrawLine(
                pen,
                new Point(topRightRect.Left, topRightRect.Top),
                new Point(topRightRect.Right, topRightRect.Top));
        }

        if (ShowHorizontalScrollBar)
        {
            // Bottom-left: only left border.
            dc.DrawLine(
                pen,
                new Point(bottomLeftRect.Left, bottomLeftRect.Top),
                new Point(bottomLeftRect.Left, bottomLeftRect.Bottom));
        }

        if (ShowVerticalScrollBar && ShowHorizontalScrollBar)
        {
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

}
