using System;
using System.Windows;
using System.Windows.Media;
using Griddo.Fields;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    /// <summary>Leading records to freeze, limited by record count and how many full records fit in the body viewport.</summary>
    private int GetEffectiveFixedRecordCount()
    {
        if (Records.Count == 0 || _viewportBodyHeight <= 0)
        {
            return 0;
        }

        var h = GetRecordHeight(0);
        if (h <= 1e-6)
        {
            return 0;
        }

        var maxFit = (int)(_viewportBodyHeight / h);
        return Math.Min(Math.Min(_fixedRecordCount, Records.Count), maxFit);
    }

    private double GetFixedRecordsHeight() => GetEffectiveFixedRecordCount() * GetRecordHeight(0);

    private double GetScrollRecordsViewportHeight() => Math.Max(0, _viewportBodyHeight - GetFixedRecordsHeight());

    private double GetScrollableRecordsContentHeight()
    {
        var h = GetRecordHeight(0);
        var f = GetEffectiveFixedRecordCount();
        return Math.Max(0, Records.Count - f) * h;
    }

    /// <summary>Top edge of a body record relative to the top of the body strip (below field headers).</summary>
    private double GetRecordBodyTopRel(int recordIndex)
    {
        var h = GetRecordHeight(0);
        var f = GetEffectiveFixedRecordCount();
        if (recordIndex < f)
        {
            return recordIndex * h;
        }

        return f * h + (recordIndex - f) * h - _verticalOffset;
    }

    private void ForEachVisibleRecord(Action<int> onRecord)
    {
        if (Records.Count == 0 || _viewportBodyHeight <= 0)
        {
            return;
        }

        var h = GetRecordHeight(0);
        var f = GetEffectiveFixedRecordCount();
        var bodyH = _viewportBodyHeight;
        for (var r = 0; r < f && r < Records.Count; r++)
        {
            if (r * h < bodyH)
            {
                onRecord(r);
            }
        }

        var scrollTop = f * h;
        var vh = bodyH - scrollTop;
        if (vh <= 0 || f >= Records.Count)
        {
            return;
        }

        var first = f + (int)Math.Floor(_verticalOffset / h);
        var last = f + (int)Math.Ceiling((_verticalOffset + vh) / h) - 1;
        first = Math.Clamp(first, f, Records.Count - 1);
        last = Math.Clamp(last, f, Records.Count - 1);
        for (var r = first; r <= last; r++)
        {
            onRecord(r);
        }
    }

    private double GetRecordHeight(int recordIndex)
    {
        _ = recordIndex;
        if (_visibleRecordCount > 0 && _viewportBodyHeight > 0)
        {
            var slots = Records.Count > 0
                ? Math.Min(_visibleRecordCount, Records.Count)
                : _visibleRecordCount;
            return _viewportBodyHeight / Math.Max(1, slots);
        }

        return Math.Max(GetMinimumRecordThickness(), _uniformRecordHeight) * ContentScale;
    }

    private void SetUniformRecordHeightFromScreen(double screenPixelHeight)
    {
        var clamped = Math.Max(GetMinimumRecordThickness(), screenPixelHeight / ContentScale);
        if (Math.Abs(_uniformRecordHeight - clamped) < double.Epsilon)
        {
            return;
        }

        _uniformRecordHeight = clamped;
        UniformRecordHeightChanged?.Invoke(this, EventArgs.Empty);
        UpdateScrollBars();
    }

    /// <summary>
    /// Fill-records mode derives height from the viewport and ignores <see cref="UniformRecordHeight"/>.
    /// Before record-divider hit math (anchor Y), switch to uniform height equal to what is currently drawn
    /// so the first mouse-move does not jump record layout under the cursor.
    /// </summary>
    private void ExitFillRecordsUsingCurrentDisplayedRecordHeight()
    {
        if (_visibleRecordCount <= 0)
        {
            return;
        }

        var hScreen = GetRecordHeight(0);
        VisibleRecordCount = 0;
        SetUniformRecordHeightFromScreen(hScreen);
    }

    private void SetRecordHeightKeepingRecordTop(int recordIndex, double newScreenHeight)
    {
        if (Records.Count == 0)
        {
            SetUniformRecordHeightFromScreen(newScreenHeight);
            return;
        }

        var clampedRecordIndex = Math.Clamp(recordIndex, 0, Records.Count - 1);
        var oldHeight = GetRecordHeight(clampedRecordIndex);
        var transposeResize = _isTransposed && Records.Count > 0 && Fields.Count > 0;
        double oldScrollExtentMax;
        double oldScrollOffset;
        if (transposeResize)
        {
            var fixedRecordsW0 = GetTransposeFixedRecordsWidth();
            var scrollVp0 = Math.Max(0, _viewportBodyWidth - fixedRecordsW0);
            var h0 = GetRecordHeight(0);
            var fr0 = GetEffectiveFixedRecordCount();
            var scrollContent0 = Math.Max(0, Records.Count - fr0) * h0;
            oldScrollExtentMax = Math.Max(0, scrollContent0 - scrollVp0);
            oldScrollOffset = _horizontalOffset;
        }
        else
        {
            oldScrollExtentMax = Math.Max(0, GetScrollableRecordsContentHeight() - GetScrollRecordsViewportHeight());
            oldScrollOffset = _verticalOffset;
        }

        // Fill-records mode (VisibleRecordCount > 0) forces record height from viewport / count and ignores
        // UniformRecordHeight. Manual divider drag must leave that mode so the dragged height applies.
        if (_visibleRecordCount > 0)
        {
            VisibleRecordCount = 0;
        }

        SetUniformRecordHeightFromScreen(newScreenHeight);

        if (transposeResize)
        {
            var fixedRecordsW = GetTransposeFixedRecordsWidth();
            var scrollRecordsViewport = Math.Max(0, _viewportBodyWidth - fixedRecordsW);
            var hAfter = GetRecordHeight(0);
            var frT = GetEffectiveFixedRecordCount();
            var scrollRecordsContent = Math.Max(0, Records.Count - frT) * hAfter;
            var newMaxHorizontal = Math.Max(0, scrollRecordsContent - scrollRecordsViewport);
            if (oldScrollExtentMax <= 1e-6 && newMaxHorizontal <= 1e-6)
            {
                return;
            }

            if (_isResizingRecord)
            {
                return;
            }

            var updatedHeightT = GetRecordHeight(clampedRecordIndex);
            var deltaHT = updatedHeightT - oldHeight;
            var frRecords = Math.Min(_fixedRecordCount, Records.Count);
            var offsetDeltaT = frRecords * deltaHT + Math.Max(0, clampedRecordIndex - frRecords) * deltaHT;
            SetHorizontalOffset(oldScrollOffset + offsetDeltaT);
            return;
        }

        var newMaxVerticalOffset = Math.Max(0, GetScrollableRecordsContentHeight() - GetScrollRecordsViewportHeight());
        if (oldScrollExtentMax <= 1e-6 && newMaxVerticalOffset <= 1e-6)
        {
            // No vertical scroll range before/after resize: keep natural layout flow.
            // Applying top-preservation offset compensation in this mode causes
            // cursor/divider drift while dragging.
            return;
        }

        // During interactive record resize, scroll compensation fights closed-form height math and
        // causes divider Y to oscillate until the body fills the viewport.
        if (_isResizingRecord)
        {
            return;
        }

        var updatedHeight = GetRecordHeight(clampedRecordIndex);
        var deltaH = updatedHeight - oldHeight;
        var fr = Math.Min(_fixedRecordCount, Records.Count);
        var offsetDelta = fr * deltaH + Math.Max(0, clampedRecordIndex - fr) * deltaH;
        SetVerticalOffset(oldScrollOffset + offsetDelta);
    }

    /// <summary>
    /// Uniform record height h so the bottom edge of record <paramref name="dividerRecordIndex"/> lies at
    /// <paramref name="bodyPointerY"/> (Y relative to top of body strip, below field headers).
    /// Uses a frozen effective fixed-record count during divider drag to avoid f(h) feedback oscillation.
    /// </summary>
    private double GetUniformRecordHeightScreenFromDividerBodyY(int dividerRecordIndex, double bodyPointerY)
    {
        if (Records.Count == 0 || _viewportBodyHeight <= 0)
        {
            return Math.Max(GetMinimumRecordThickness() * ContentScale, bodyPointerY);
        }

        var k = Math.Clamp(dividerRecordIndex, 0, Records.Count - 1);
        // Must match live layout (frozen + scroll); a frozen snapshot can disagree with
        // GetRecordBodyTopRel when effective fixed-record count changes with record height.
        var f = GetEffectiveFixedRecordCount();
        var hScreen = k < f
            ? bodyPointerY / (k + 1)
            : (bodyPointerY + _verticalOffset) / (k + 1);
        return Math.Max(GetMinimumRecordThickness() * ContentScale, hScreen);
    }

    /// <summary>
    /// Uniform record height h so the right edge of record <paramref name="dividerRecordIndex"/> lies at
    /// <paramref name="bodyPointerX"/> (X relative to the left edge of the body strip, after record headers).
    /// Mirror of <see cref="GetUniformRecordHeightScreenFromDividerBodyY"/> for transposed layout (records scroll horizontally).
    /// </summary>
    private double GetUniformRecordHeightScreenFromDividerBodyX(int dividerRecordIndex, double bodyPointerX)
    {
        if (Records.Count == 0 || _viewportBodyWidth <= 0)
        {
            return Math.Max(GetMinimumRecordThickness() * ContentScale, bodyPointerX);
        }

        var k = Math.Clamp(dividerRecordIndex, 0, Records.Count - 1);
        var f = GetEffectiveFixedRecordCount();
        var hScreen = k < f
            ? bodyPointerX / (k + 1)
            : (bodyPointerX + _horizontalOffset) / (k + 1);
        return Math.Max(GetMinimumRecordThickness() * ContentScale, hScreen);
    }

    /// <summary>One-shot scroll adjustment after interactive record-height drag (see <see cref="SetRecordHeightKeepingRecordTop"/>).</summary>
    private void ApplyInteractiveRecordResizeScrollPreservation(int dividerRecordIndex, double recordHeightAtDragStart, double scrollOffsetAtDragStart)
    {
        if (Records.Count == 0 || dividerRecordIndex < 0)
        {
            return;
        }

        if (IsBodyTransposed)
        {
            var fixedRecordsW = GetTransposeFixedRecordsWidth();
            var scrollRecordsViewport = Math.Max(0, _viewportBodyWidth - fixedRecordsW);
            var h = GetRecordHeight(0);
            var frT = GetEffectiveFixedRecordCount();
            var scrollRecordsContent = Math.Max(0, Records.Count - frT) * h;
            var maxH = Math.Max(0, scrollRecordsContent - scrollRecordsViewport);
            if (maxH <= 1e-6)
            {
                return;
            }

            var clampedT = Math.Clamp(dividerRecordIndex, 0, Records.Count - 1);
            var newHt = GetRecordHeight(clampedT);
            var deltaHt = newHt - recordHeightAtDragStart;
            if (Math.Abs(deltaHt) < 1e-9)
            {
                return;
            }

            var frRecords = Math.Min(_fixedRecordCount, Records.Count);
            var offsetDeltaT = frRecords * deltaHt + Math.Max(0, clampedT - frRecords) * deltaHt;
            SetHorizontalOffset(scrollOffsetAtDragStart + offsetDeltaT);
            return;
        }

        var maxV = Math.Max(0, GetScrollableRecordsContentHeight() - GetScrollRecordsViewportHeight());
        if (maxV <= 1e-6)
        {
            return;
        }

        var clamped = Math.Clamp(dividerRecordIndex, 0, Records.Count - 1);
        var newH = GetRecordHeight(clamped);
        var deltaH = newH - recordHeightAtDragStart;
        if (Math.Abs(deltaH) < 1e-9)
        {
            return;
        }

        var fr = Math.Min(_fixedRecordCount, Records.Count);
        var offsetDelta = fr * deltaH + Math.Max(0, clamped - fr) * deltaH;
        SetVerticalOffset(scrollOffsetAtDragStart + offsetDelta);
    }

    private void AutoSizeRecord(int recordIndex)
    {
        if (recordIndex < 0 || recordIndex >= Records.Count)
        {
            return;
        }

        var typeface = new Typeface("Segoe UI");
        var pad = 6 * _contentScale;
        var max = MeasureTextHeight((recordIndex + 1).ToString(), typeface, EffectiveFontSize) + pad;
        foreach (var fieldView in Fields)
        {
            var value = fieldView.GetValue(Records[recordIndex]);
            max = Math.Max(max, MeasureCellHeight(value, typeface, EffectiveFontSize) + pad);
        }

        SetRecordHeightKeepingRecordTop(recordIndex, max);
        InvalidateVisual();
    }

    private Rect GetCellRect(int record, int col)
    {
        if (record < 0 || record >= Records.Count || col < 0 || col >= Fields.Count)
        {
            return Rect.Empty;
        }

        if (IsBodyTransposed)
        {
            var tx = _recordHeaderWidth + GetTransposedRecordBodyLeftRel(record);
            var ty = ScaledFieldHeaderHeight + GetTransposedFieldBodyTopRel(col);
            return new Rect(tx, ty, GetRecordHeight(record), GetFieldWidth(col));
        }

        double x;
        if (col < _fixedFieldCount)
        {
            x = _recordHeaderWidth;
            for (var i = 0; i < col; i++)
            {
                x += GetFieldWidth(i);
            }
        }
        else
        {
            x = _recordHeaderWidth + GetFixedFieldsWidth();
            for (var i = _fixedFieldCount; i < col; i++)
            {
                x += GetFieldWidth(i);
            }

            x -= _horizontalOffset;
        }

        var y = ScaledFieldHeaderHeight + GetRecordBodyTopRel(record);
        return new Rect(
            x,
            y,
            GetFieldWidth(col),
            GetRecordHeight(record));
    }
}
