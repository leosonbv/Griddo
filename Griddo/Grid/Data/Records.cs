using System.Windows;
using System.Windows.Media;

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
        if (_isResizingRecord && _resizeEffectiveFixedRecordCount >= 0)
        {
            return Math.Clamp(_resizeEffectiveFixedRecordCount, 0, Records.Count);
        }

        return Math.Min(Math.Min((int)_fixedRecordCount, (int)Records.Count), maxFit);
    }

    /// <summary>
    /// How many row bands the viewport height is split into in fill-records mode.
    /// Fewer records than the fill count share the full viewport; more records use the fill count and scroll.
    /// </summary>
    private int GetFillViewportSlotCount()
    {
        if (_visibleRecordCount <= 0)
        {
            return 0;
        }

        if (Records.Count <= 0)
        {
            return _visibleRecordCount;
        }

        if (Records.Count > _visibleRecordCount)
        {
            return (int)_visibleRecordCount;
        }

        return Math.Min((int)_visibleRecordCount, Records.Count);
    }

    private bool IsFillRecordMode() =>
        _visibleRecordCount > 0 && _viewportBodyHeight > 0 && Records.Count > 0;

    private void GetFillRecordHeightParts(out int slots, out double baseHeight, out int extraRows)
    {
        slots = GetFillViewportSlotCount();
        if (slots <= 0 || _viewportBodyHeight <= 0)
        {
            baseHeight = 0;
            extraRows = 0;
            return;
        }

        baseHeight = Math.Floor(_viewportBodyHeight / slots);
        extraRows = (int)Math.Round(_viewportBodyHeight - (baseHeight * slots), MidpointRounding.AwayFromZero);
        extraRows = Math.Clamp(extraRows, 0, slots);
    }

    private int GetFillSlotForRecord(int recordIndex)
    {
        GetFillRecordHeightParts(out var slots, out _, out _);
        var idx = Math.Clamp(recordIndex, 0, Records.Count - 1);
        if (Records.Count <= (int)_visibleRecordCount)
        {
            return idx;
        }

        return idx % slots;
    }

    private double SumFillDistributedHeightsBefore(int recordIndex)
    {
        var end = Math.Clamp(recordIndex, 0, Records.Count);
        var sum = 0.0;
        for (var r = 0; r < end; r++)
        {
            sum += GetRecordHeight(r);
        }

        return sum;
    }

    private double GetFixedRecordsHeight()
    {
        if (IsFillRecordMode())
        {
            return SumFillDistributedHeightsBefore(GetEffectiveFixedRecordCount());
        }

        return GetEffectiveFixedRecordCount() * GetRecordHeight(0);
    }

    private double GetScrollRecordsViewportHeight() => Math.Max(0, _viewportBodyHeight - GetFixedRecordsHeight());

    private double GetScrollableRecordsContentHeight()
    {
        var f = GetEffectiveFixedRecordCount();
        if (IsFillRecordMode())
        {
            return Math.Max(0, SumFillDistributedHeightsBefore(Records.Count) - SumFillDistributedHeightsBefore(f));
        }

        var h = GetRecordHeight(0);
        return Math.Max(0, Records.Count - f) * h;
    }

    /// <summary>Top edge of a body record relative to the top of the body strip (below field headers).</summary>
    private double GetRecordBodyTopRel(int recordIndex)
    {
        var f = GetEffectiveFixedRecordCount();
        if (IsFillRecordMode())
        {
            var top = SumFillDistributedHeightsBefore(recordIndex);
            if (recordIndex >= f)
            {
                top -= _verticalOffset;
            }

            return top;
        }

        var h = GetRecordHeight(0);
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

        if (IsFillRecordMode())
        {
            var f = GetEffectiveFixedRecordCount();
            var bodyH = _viewportBodyHeight;
            var scrollBandTop = SumFillDistributedHeightsBefore(f);
            var scrollViewport = Math.Max(0, bodyH - scrollBandTop);
            for (var r = 0; r < Records.Count; r++)
            {
                var top = SumFillDistributedHeightsBefore(r);
                var bottom = top + GetRecordHeight(r);
                if (r < f)
                {
                    if (top < bodyH)
                    {
                        onRecord(r);
                    }

                    continue;
                }

                var relTop = top - scrollBandTop - _verticalOffset;
                var relBottom = bottom - scrollBandTop - _verticalOffset;
                if (relBottom > 0 && relTop < scrollViewport)
                {
                    onRecord(r);
                }
            }

            return;
        }

        var h = GetRecordHeight(0);
        var fixedCount = GetEffectiveFixedRecordCount();
        var bodyHeight = _viewportBodyHeight;
        for (var r = 0; r < fixedCount && r < Records.Count; r++)
        {
            if (r * h < bodyHeight)
            {
                onRecord(r);
            }
        }

        var scrollTop = fixedCount * h;
        var viewportH = bodyHeight - scrollTop;
        if (viewportH <= 0 || fixedCount >= Records.Count)
        {
            return;
        }

        var first = fixedCount + (int)Math.Floor(_verticalOffset / h);
        var last = fixedCount + (int)Math.Ceiling((_verticalOffset + viewportH) / h) - 1;
        first = Math.Clamp(first, fixedCount, Records.Count - 1);
        last = Math.Clamp(last, fixedCount, Records.Count - 1);
        for (var r = first; r <= last; r++)
        {
            onRecord(r);
        }
    }

    private double GetRecordHeight(int recordIndex)
    {
        if (IsFillRecordMode())
        {
            GetFillRecordHeightParts(out _, out var baseHeight, out var extraRows);
            var slot = GetFillSlotForRecord(recordIndex);
            return baseHeight + (slot < extraRows ? 1 : 0);
        }

        _ = recordIndex;
        return Math.Max(GetMinimumRecordThickness(), _uniformRecordHeight) * ContentScale;
    }

    private int ResolveRecordIndexFromBodyY(double bodyY)
    {
        if (Records.Count == 0 || bodyY < 0)
        {
            return -1;
        }

        if (IsFillRecordMode())
        {
            for (var r = 0; r < Records.Count; r++)
            {
                var top = GetRecordBodyTopRel(r);
                var bottom = top + GetRecordHeight(r);
                if (bodyY >= top && bodyY < bottom)
                {
                    return r;
                }
            }

            return Records.Count - 1;
        }

        var h = GetRecordHeight(0);
        var f = GetEffectiveFixedRecordCount();
        var fixedH = f * h;
        if (bodyY < fixedH)
        {
            var record = (int)(bodyY / h);
            return record >= 0 && record < Records.Count ? record : -1;
        }

        var scrollBodyY = bodyY - fixedH;
        var scrollContentY = scrollBodyY + _verticalOffset;
        var scrollRecord = f + (int)(scrollContentY / h);
        return scrollRecord >= 0 && scrollRecord < Records.Count ? scrollRecord : -1;
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
    /// Fill-records mode derives height from the viewport and ignores <see cref="Grid.Griddo.UniformRecordHeight"/>.
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
            var frRecords = Math.Min((int)_fixedRecordCount, (int)Records.Count);
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
        var fr = Math.Min((int)_fixedRecordCount, (int)Records.Count);
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

    /// <summary>First record row/column band aligned with the top/left edge of the body viewport.</summary>
    private int GetTopVisibleRecordInViewport()
    {
        if (Records.Count == 0)
        {
            return 0;
        }

        var f = GetEffectiveFixedRecordCount();
        if (f > 0)
        {
            return 0;
        }

        var h = GetRecordHeight(0);
        if (h <= 1e-6)
        {
            return 0;
        }

        var offset = IsBodyTransposed ? _horizontalOffset : _verticalOffset;
        var first = (int)Math.Floor(offset / h + 1e-9);
        return Math.Clamp(first, 0, Records.Count - 1);
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
                SetHorizontalOffset(0);
                return;
            }

            var clampedT = Math.Clamp(dividerRecordIndex, 0, Records.Count - 1);
            var newHt = GetRecordHeight(clampedT);
            var deltaHt = newHt - recordHeightAtDragStart;
            if (Math.Abs(deltaHt) < 1e-9)
            {
                return;
            }

            var frRecords = Math.Min((int)_fixedRecordCount, (int)Records.Count);
            var offsetDeltaT = frRecords * deltaHt + Math.Max(0, clampedT - frRecords) * deltaHt;
            if (clampedT == GetTopVisibleRecordInViewport())
            {
                offsetDeltaT = 0;
            }

            var targetT = Math.Clamp(scrollOffsetAtDragStart + offsetDeltaT, 0, maxH);
            if (newHt > 1e-6)
            {
                targetT = FloorToRecordStep(targetT, newHt);
                if (ShouldSnapToTrailingEdge(targetT, maxH, newHt))
                {
                    targetT = maxH;
                }
            }

            SetHorizontalOffset(targetT);
            return;
        }

        var maxV = Math.Max(0, GetScrollableRecordsContentHeight() - GetScrollRecordsViewportHeight());
        if (maxV <= 1e-6)
        {
            SetVerticalOffset(0);
            return;
        }

        var clamped = Math.Clamp(dividerRecordIndex, 0, Records.Count - 1);
        var newH = GetRecordHeight(clamped);
        var deltaH = newH - recordHeightAtDragStart;
        if (Math.Abs(deltaH) < 1e-9)
        {
            return;
        }

        var fr = Math.Min((int)_fixedRecordCount, (int)Records.Count);
        var offsetDelta = fr * deltaH + Math.Max(0, clamped - fr) * deltaH;
        if (clamped == GetTopVisibleRecordInViewport())
        {
            offsetDelta = 0;
        }

        var target = Math.Clamp(scrollOffsetAtDragStart + offsetDelta, 0, maxV);
        if (newH > 1e-6)
        {
            target = FloorToRecordStep(target, newH);
            if (ShouldSnapToTrailingEdge(target, maxV, newH))
            {
                target = maxV;
            }
        }

        SetVerticalOffset(target);
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
