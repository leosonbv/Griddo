using System.Windows.Input;
using Griddo.Primitives;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    private void ClearHeaderAuxiliarySelectionState()
    {
        _fieldHeaderOnlySelection.Clear();
        _recordHeaderOnlySelection.Clear();
        _fieldHeaderRightClickOutline.Clear();
        _recordHeaderRightClickOutline.Clear();
    }

    private bool IsFieldHeaderMarkedSelected(int fieldIndex) =>
        IsFieldSelected(fieldIndex) || _fieldHeaderOnlySelection.Contains(fieldIndex);

    private bool IsRecordHeaderMarkedSelected(int recordIndex) =>
        IsRecordSelected(recordIndex) || _recordHeaderOnlySelection.Contains(recordIndex);

    private void MoveCurrentCell(int recordDelta, int colDelta)
    {
        if (Records.Count == 0 || Fields.Count == 0)
        {
            return;
        }

        ClearHeaderFocus();
        ClearHeaderAuxiliarySelectionState();
        var record = Math.Clamp(_currentCell.RecordIndex + recordDelta, 0, Records.Count - 1);
        var col = Math.Clamp(_currentCell.FieldIndex + colDelta, 0, Fields.Count - 1);
        AssignCurrentCell(new GriddoCellAddress(record, col));
        _selectedCells.Clear();
        _selectedCells.Add(_currentCell);
        InvalidateVisual();
    }

    private bool HandleCellKeyboardNavigation(Key key, bool isCtrlPressed, bool isShiftPressed)
    {
        if (!GriddoCellNavigator.TryGetTarget(key, isCtrlPressed, _currentCell, Records.Count, Fields.Count, out var target))
        {
            return false;
        }

        ApplyKeyboardNavigationTarget(target, isShiftPressed);
        return true;
    }

    private void ApplyKeyboardNavigationTarget(GriddoCellAddress target, bool isShiftPressed)
    {
        if (isShiftPressed)
        {
            if (!_hasKeyboardSelectionAnchor)
            {
                _keyboardSelectionAnchor = _currentCell;
                _hasKeyboardSelectionAnchor = true;
            }

            ClearHeaderFocus();
            ClearHeaderAuxiliarySelectionState();
            AssignCurrentCell(target);
            SelectRange(_keyboardSelectionAnchor, _currentCell, additive: false);
            InvalidateVisual();
            return;
        }

        ClearHeaderFocus();
        ClearHeaderAuxiliarySelectionState();
        _hasKeyboardSelectionAnchor = false;
        AssignCurrentCell(target);
        _selectedCells.Clear();
        _selectedCells.Add(_currentCell);
        InvalidateVisual();
    }

    private void ApplyDragSelection()
    {
        ClearHeaderAuxiliarySelectionState();
        _selectedCells.Clear();
        if (_dragIsAdditive || (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _selectedCells.UnionWith(_selectionDragSnapshot);
        }

        var minRecord = Math.Min(_dragAnchorCell.RecordIndex, _dragCurrentCell.RecordIndex);
        var maxRecord = Math.Max(_dragAnchorCell.RecordIndex, _dragCurrentCell.RecordIndex);
        var minCol = Math.Min(_dragAnchorCell.FieldIndex, _dragCurrentCell.FieldIndex);
        var maxCol = Math.Max(_dragAnchorCell.FieldIndex, _dragCurrentCell.FieldIndex);

        for (var record = minRecord; record <= maxRecord; record++)
        {
            for (var col = minCol; col <= maxCol; col++)
            {
                _selectedCells.Add(new GriddoCellAddress(record, col));
            }
        }
    }

    private void SelectRange(GriddoCellAddress from, GriddoCellAddress to, bool additive)
    {
        ClearHeaderAuxiliarySelectionState();
        if (!additive)
        {
            _selectedCells.Clear();
        }

        var minRecord = Math.Min(from.RecordIndex, to.RecordIndex);
        var maxRecord = Math.Max(from.RecordIndex, to.RecordIndex);
        var minCol = Math.Min(from.FieldIndex, to.FieldIndex);
        var maxCol = Math.Max(from.FieldIndex, to.FieldIndex);
        for (var record = minRecord; record <= maxRecord; record++)
        {
            for (var col = minCol; col <= maxCol; col++)
            {
                if (record >= 0 && record < Records.Count && col >= 0 && col < Fields.Count)
                {
                    _selectedCells.Add(new GriddoCellAddress(record, col));
                }
            }
        }
    }

    private void SelectField(int fieldIndex, bool additive)
    {
        if (fieldIndex < 0 || fieldIndex >= Fields.Count)
        {
            return;
        }

        ClearHeaderAuxiliarySelectionState();
        if (!additive)
        {
            _selectedCells.Clear();
        }

        for (var record = 0; record < Records.Count; record++)
        {
            _selectedCells.Add(new GriddoCellAddress(record, fieldIndex));
        }
    }

    private void SelectAllCells()
    {
        ClearHeaderFocus();
        ClearHeaderAuxiliarySelectionState();
        _selectedCells.Clear();
        if (Records.Count == 0 || Fields.Count == 0)
        {
            return;
        }

        for (var record = 0; record < Records.Count; record++)
        {
            for (var col = 0; col < Fields.Count; col++)
            {
                _selectedCells.Add(new GriddoCellAddress(record, col));
            }
        }
    }

    private void ClearHeaderFocus()
    {
        _headerFocusKind = HeaderFocusKind.None;
    }

    private void StopFieldMoveTracking()
    {
        _isTrackingFieldMove = false;
        _isMovingField = false;
        _isMovingPointerInFieldHeader = false;
        _fieldMoveStartedFromSelectedHeader = false;
        _pendingFieldHeaderSelectionOnMouseUp = false;
        _pendingFieldHeaderIndex = -1;
        _pendingFieldHeaderPreserveSelection = false;
        _movingFieldIndex = -1;
        _fieldMoveCueIndex = -1;
        if (!_isDraggingSelection
            && !_isDraggingFieldHeaderSelection
            && !_isDraggingRecordHeaderSelection
            && !_isResizingField
            && !_isResizingRecord
            && !_isTrackingRecordMove
            && IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }

    private void StopRecordMoveTracking()
    {
        _isTrackingRecordMove = false;
        _isMovingRecord = false;
        _movingRecordIndex = -1;
        _recordMoveCueIndex = -1;
        _pendingRecordHeaderSelectionOnMouseUp = false;
        _pendingRecordHeaderIndex = -1;
        _pendingRecordHeaderPreserveSelection = false;
        if (!_isDraggingSelection
            && !_isDraggingFieldHeaderSelection
            && !_isDraggingRecordHeaderSelection
            && !_isResizingField
            && !_isResizingRecord
            && !_isTrackingFieldMove
            && IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }

    private void RemapHeaderFocusFieldAfterMove(int fromIndex, int toIndex)
    {
        if (_headerFocusKind != HeaderFocusKind.Field)
        {
            return;
        }

        _headerFocusFieldIndex = RemapFieldIndex(_headerFocusFieldIndex, fromIndex, toIndex);
    }

    private void MoveField(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || toIndex < 0 || fromIndex >= Fields.Count || toIndex >= Fields.Count || fromIndex == toIndex)
        {
            return;
        }

        Fields.Move(fromIndex, toIndex);

        var oldCurrent = _currentCell;
        AssignCurrentCell(new GriddoCellAddress(oldCurrent.RecordIndex, RemapFieldIndex(oldCurrent.FieldIndex, fromIndex, toIndex)));

        var remapped = new HashSet<GriddoCellAddress>();
        foreach (var address in _selectedCells)
        {
            remapped.Add(new GriddoCellAddress(address.RecordIndex, RemapFieldIndex(address.FieldIndex, fromIndex, toIndex)));
        }

        _selectedCells.Clear();
        _selectedCells.UnionWith(remapped);
        if (_fieldHeaderOnlySelection.Count > 0)
        {
            var remappedHeaders = new HashSet<int>();
            foreach (var c in _fieldHeaderOnlySelection)
            {
                remappedHeaders.Add(RemapFieldIndex(c, fromIndex, toIndex));
            }

            _fieldHeaderOnlySelection.Clear();
            foreach (var c in remappedHeaders)
            {
                if (c >= 0 && c < Fields.Count)
                {
                    _fieldHeaderOnlySelection.Add(c);
                }
            }
        }

        if (_fieldHeaderRightClickOutline.Count > 0)
        {
            var remappedOutline = new HashSet<int>();
            foreach (var c in _fieldHeaderRightClickOutline)
            {
                remappedOutline.Add(RemapFieldIndex(c, fromIndex, toIndex));
            }

            _fieldHeaderRightClickOutline.Clear();
            foreach (var c in remappedOutline)
            {
                if (c >= 0 && c < Fields.Count)
                {
                    _fieldHeaderRightClickOutline.Add(c);
                }
            }
        }

        RemapHeaderFocusFieldAfterMove(fromIndex, toIndex);
        var oldToNew = new int[Fields.Count];
        for (var i = 0; i < oldToNew.Length; i++)
        {
            oldToNew[i] = RemapFieldIndex(i, fromIndex, toIndex);
        }

        RemapSortDescriptorsAfterFieldMove(oldToNew);
    }

    private void MoveSelectedFields(int anchorField, int targetField)
    {
        var selected = GetSelectedFieldIndices();
        if (selected.Count <= 1 || !selected.Contains(anchorField))
        {
            MoveField(anchorField, targetField);
            return;
        }

        var minSelected = selected[0];
        var maxSelected = selected[^1];
        var insertAfterTarget = targetField > maxSelected;
        if (targetField >= minSelected && targetField <= maxSelected)
        {
            return;
        }

        var oldToNew = MoveFieldOrRecordIndices(
            Fields.Count,
            selected,
            targetField,
            insertAfterTarget,
            index => Fields.Move(index.from, index.to));
        RemapSelectionAfterFieldMove(oldToNew);
        RemapSortDescriptorsAfterFieldMove(oldToNew);
    }

    private void MoveSelectedRecords(int anchorRecord, int targetRecord)
    {
        var selected = GetSelectedRecordIndices();
        if (selected.Count == 0 || !selected.Contains(anchorRecord))
        {
            return;
        }

        var minSelected = selected[0];
        var maxSelected = selected[^1];
        var insertAfterTarget = targetRecord > maxSelected;
        if (targetRecord >= minSelected && targetRecord <= maxSelected)
        {
            return;
        }

        var oldToNew = MoveFieldOrRecordIndices(
            Records.Count,
            selected,
            targetRecord,
            insertAfterTarget,
            index => Records.Move(index.from, index.to));
        RemapSelectionAfterRecordMove(oldToNew);
    }

    /// <summary>Clears all selected cells without changing the logical current cell; ends in-cell editing.</summary>
    public void ClearCellSelection()
    {
        ClearHeaderFocus();
        ClearHeaderAuxiliarySelectionState();
        _hasKeyboardSelectionAnchor = false;
        _selectedCells.Clear();
        _isEditing = false;
        if (Records.Count > 0 && Fields.Count > 0)
        {
            AssignCurrentCell(new GriddoCellAddress(
                Math.Clamp(_currentCell.RecordIndex, 0, Records.Count - 1),
                Math.Clamp(_currentCell.FieldIndex, 0, Fields.Count - 1)));
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Sets the current body cell and replaces the selection with that single cell (no header selection).
    /// Used when reloading grid data while restoring focus to a logical row/column.
    /// </summary>
    public void SetBodyCellNavigationTarget(int recordIndex, int fieldIndex)
    {
        if (Records.Count == 0 || Fields.Count == 0)
        {
            return;
        }

        ClearHeaderFocus();
        ClearHeaderAuxiliarySelectionState();
        _hasKeyboardSelectionAnchor = false;
        _isEditing = false;
        recordIndex = Math.Clamp(recordIndex, 0, Records.Count - 1);
        fieldIndex = Math.Clamp(fieldIndex, 0, Fields.Count - 1);
        AssignCurrentCell(new GriddoCellAddress(recordIndex, fieldIndex));
        _selectedCells.Clear();
        _selectedCells.Add(_currentCell);
        InvalidateVisual();
    }

    /// <summary>
    /// Restores the current body cell and selected body cells after grid records are reloaded.
    /// </summary>
    public void RestoreBodyCellSelection(
        int currentRecordIndex,
        int currentFieldIndex,
        IReadOnlyCollection<GriddoCellAddress> selectedCells)
    {
        if (Records.Count == 0 || Fields.Count == 0)
        {
            return;
        }

        ClearHeaderFocus();
        ClearHeaderAuxiliarySelectionState();
        _hasKeyboardSelectionAnchor = false;
        _isEditing = false;

        currentRecordIndex = Math.Clamp(currentRecordIndex, 0, Records.Count - 1);
        currentFieldIndex = Math.Clamp(currentFieldIndex, 0, Fields.Count - 1);
        AssignCurrentCell(new GriddoCellAddress(currentRecordIndex, currentFieldIndex));

        _selectedCells.Clear();
        if (selectedCells.Count > 0)
        {
            foreach (var address in selectedCells)
            {
                if (address.RecordIndex < 0 || address.RecordIndex >= Records.Count)
                {
                    continue;
                }

                var fieldIndex = Math.Clamp(address.FieldIndex, 0, Fields.Count - 1);
                _selectedCells.Add(new GriddoCellAddress(address.RecordIndex, fieldIndex));
            }
        }

        if (_selectedCells.Count == 0)
        {
            _selectedCells.Add(_currentCell);
        }
        else if (!_selectedCells.Contains(_currentCell))
        {
            _selectedCells.Add(_currentCell);
        }

        InvalidateVisual();
    }

    /// <summary>Moves all selected records up (<paramref name="direction"/> = -1) or down (+1), matching record-header drag behavior.</summary>
    /// <returns>True if a move was applied.</returns>
    public bool TryMoveSelectedRecordsStep(int direction)
    {
        if (direction is not (-1) and not 1 || Records.Count == 0)
        {
            return false;
        }

        var selected = GetSelectedRecordIndices();
        if (selected.Count == 0)
        {
            return false;
        }

        if (direction < 0)
        {
            var min = selected[0];
            if (min <= 0)
            {
                return false;
            }

            MoveSelectedRecords(min, min - 1);
        }
        else
        {
            var max = selected[^1];
            if (max >= Records.Count - 1)
            {
                return false;
            }

            MoveSelectedRecords(max, max + 1);
        }

        InvalidateVisual();
        return true;
    }

    private static int[] MoveFieldOrRecordIndices(
        int count,
        List<int> selectedIndices,
        int targetIndex,
        bool insertAfterTarget,
        Action<(int from, int to)> mover)
    {
        if (count <= 0 || selectedIndices.Count == 0)
        {
            return [];
        }

        selectedIndices.Sort();
        var selectedLookup = selectedIndices.ToHashSet();
        var unselected = Enumerable.Range(0, count)
            .Where(i => !selectedLookup.Contains(i))
            .ToList();
        var insertionPoint = insertAfterTarget
            ? unselected.Count(i => i <= targetIndex)
            : unselected.Count(i => i < targetIndex);
        insertionPoint = Math.Clamp(insertionPoint, 0, unselected.Count);

        var finalOrder = new List<int>(count);
        finalOrder.AddRange(unselected.Take(insertionPoint));
        finalOrder.AddRange(selectedIndices);
        finalOrder.AddRange(unselected.Skip(insertionPoint));

        var currentOrder = Enumerable.Range(0, count).ToList();
        var itemPositionOfKey = new int[count];
        for (var p = 0; p < currentOrder.Count; p++)
        {
            itemPositionOfKey[currentOrder[p]] = p;
        }

        for (var newIndex = 0; newIndex < finalOrder.Count; newIndex++)
        {
            var item = finalOrder[newIndex];
            var currentIndex = itemPositionOfKey[item];
            if (currentIndex < 0 || currentIndex == newIndex)
            {
                continue;
            }

            mover((currentIndex, newIndex));
            currentOrder.RemoveAt(currentIndex);
            currentOrder.Insert(newIndex, item);
            for (var j = 0; j < currentOrder.Count; j++)
            {
                itemPositionOfKey[currentOrder[j]] = j;
            }
        }

        var oldToNew = new int[count];
        for (var newIndex = 0; newIndex < finalOrder.Count; newIndex++)
        {
            oldToNew[finalOrder[newIndex]] = newIndex;
        }

        return oldToNew;
    }

    private void RemapSelectionAfterFieldMove(int[] oldToNew)
    {
        if (oldToNew.Length == 0)
        {
            return;
        }

        if (_currentCell is { IsValid: true, FieldIndex: >= 0 }
            && _currentCell.FieldIndex < oldToNew.Length)
        {
            AssignCurrentCell(new GriddoCellAddress(_currentCell.RecordIndex, oldToNew[_currentCell.FieldIndex]));
        }

        var remapped = new HashSet<GriddoCellAddress>();
        foreach (var address in _selectedCells)
        {
            if (address.FieldIndex >= 0 && address.FieldIndex < oldToNew.Length)
            {
                remapped.Add(new GriddoCellAddress(address.RecordIndex, oldToNew[address.FieldIndex]));
            }
        }

        _selectedCells.Clear();
        _selectedCells.UnionWith(remapped);

        if (_headerFocusKind == HeaderFocusKind.Field
            && _headerFocusFieldIndex >= 0
            && _headerFocusFieldIndex < oldToNew.Length)
        {
            _headerFocusFieldIndex = oldToNew[_headerFocusFieldIndex];
        }

        if (_fieldHeaderOnlySelection.Count > 0)
        {
            var remappedHeaders = new HashSet<int>();
            foreach (var c in _fieldHeaderOnlySelection)
            {
                if (c >= 0 && c < oldToNew.Length)
                {
                    remappedHeaders.Add(oldToNew[c]);
                }
            }

            _fieldHeaderOnlySelection.Clear();
            foreach (var c in remappedHeaders)
            {
                _fieldHeaderOnlySelection.Add(c);
            }
        }

        if (_fieldHeaderRightClickOutline.Count > 0)
        {
            var remappedOutline = new HashSet<int>();
            foreach (var c in _fieldHeaderRightClickOutline)
            {
                if (c >= 0 && c < oldToNew.Length)
                {
                    remappedOutline.Add(oldToNew[c]);
                }
            }

            _fieldHeaderRightClickOutline.Clear();
            foreach (var c in remappedOutline)
            {
                _fieldHeaderRightClickOutline.Add(c);
            }
        }
    }

    private void RemapSelectionAfterRecordMove(int[] oldToNew)
    {
        if (oldToNew.Length == 0)
        {
            return;
        }

        if (_currentCell is { IsValid: true, RecordIndex: >= 0 }
            && _currentCell.RecordIndex < oldToNew.Length)
        {
            AssignCurrentCell(new GriddoCellAddress(oldToNew[_currentCell.RecordIndex], _currentCell.FieldIndex));
        }

        var remapped = new HashSet<GriddoCellAddress>();
        foreach (var address in _selectedCells)
        {
            if (address.RecordIndex >= 0 && address.RecordIndex < oldToNew.Length)
            {
                remapped.Add(new GriddoCellAddress(oldToNew[address.RecordIndex], address.FieldIndex));
            }
        }

        _selectedCells.Clear();
        _selectedCells.UnionWith(remapped);

        if (_headerFocusKind == HeaderFocusKind.Record
            && _headerFocusRecordIndex >= 0
            && _headerFocusRecordIndex < oldToNew.Length)
        {
            _headerFocusRecordIndex = oldToNew[_headerFocusRecordIndex];
        }

        if (_recordHeaderOnlySelection.Count > 0)
        {
            var remappedRecords = new HashSet<int>();
            foreach (var r in _recordHeaderOnlySelection)
            {
                if (r >= 0 && r < oldToNew.Length)
                {
                    remappedRecords.Add(oldToNew[r]);
                }
            }

            _recordHeaderOnlySelection.Clear();
            foreach (var r in remappedRecords)
            {
                _recordHeaderOnlySelection.Add(r);
            }
        }

        if (_recordHeaderRightClickOutline.Count > 0)
        {
            var remappedOutline = new HashSet<int>();
            foreach (var r in _recordHeaderRightClickOutline)
            {
                if (r >= 0 && r < oldToNew.Length)
                {
                    remappedOutline.Add(oldToNew[r]);
                }
            }

            _recordHeaderRightClickOutline.Clear();
            foreach (var r in remappedOutline)
            {
                _recordHeaderRightClickOutline.Add(r);
            }
        }

        if (_hasKeyboardSelectionAnchor
            && _keyboardSelectionAnchor.RecordIndex >= 0
            && _keyboardSelectionAnchor.RecordIndex < oldToNew.Length)
        {
            _keyboardSelectionAnchor = new GriddoCellAddress(
                oldToNew[_keyboardSelectionAnchor.RecordIndex],
                _keyboardSelectionAnchor.FieldIndex);
        }
    }

    private static int RemapFieldIndex(int fieldIndex, int fromIndex, int toIndex)
    {
        if (fieldIndex == fromIndex)
        {
            return toIndex;
        }

        if (fromIndex < toIndex)
        {
            return (fieldIndex > fromIndex && fieldIndex <= toIndex) ? fieldIndex - 1 : fieldIndex;
        }

        return (fieldIndex >= toIndex && fieldIndex < fromIndex) ? fieldIndex + 1 : fieldIndex;
    }

    private void SelectRecord(int recordIndex, bool additive)
    {
        if (recordIndex < 0 || recordIndex >= Records.Count)
        {
            return;
        }

        ClearHeaderAuxiliarySelectionState();
        if (!additive)
        {
            _selectedCells.Clear();
        }

        for (var col = 0; col < Fields.Count; col++)
        {
            _selectedCells.Add(new GriddoCellAddress(recordIndex, col));
        }
    }

    /// <summary>Selects one entire record (all fields), optionally adding to the existing selection.</summary>
    public void SelectEntireRecord(int recordIndex, bool additive = false)
    {
        ClearHeaderFocus();
        SelectRecord(recordIndex, additive);
        if (recordIndex >= 0 && recordIndex < Records.Count && Fields.Count > 0)
        {
            AssignCurrentCell(new GriddoCellAddress(
                recordIndex,
                Math.Clamp(_currentCell.FieldIndex, 0, Fields.Count - 1)));
        }

        _hasKeyboardSelectionAnchor = false;
        _isEditing = false;
        InvalidateVisual();
    }

    /// <summary>Selects one entire field (all records), optionally adding to the existing selection.</summary>
    public void SelectEntireField(int fieldIndex, bool additive = false)
    {
        ClearHeaderFocus();
        SelectField(fieldIndex, additive);
        if (fieldIndex >= 0 && fieldIndex < Fields.Count && Records.Count > 0)
        {
            AssignCurrentCell(new GriddoCellAddress(
                Math.Clamp(_currentCell.RecordIndex, 0, Records.Count - 1),
                fieldIndex));
        }

        _hasKeyboardSelectionAnchor = false;
        _isEditing = false;
        InvalidateVisual();
    }

    private bool IsRecordSelected(int recordIndex)
    {
        return _selectedCells.Any(c => c.RecordIndex == recordIndex);
    }

    private bool IsFieldSelected(int fieldIndex)
    {
        return _selectedCells.Any(c => c.FieldIndex == fieldIndex);
    }
    private List<int> GetSelectedFieldIndices()
    {
        var set = new HashSet<int>();
        foreach (var address in _selectedCells)
        {
            var c = address.FieldIndex;
            if (c >= 0 && c < Fields.Count)
            {
                set.Add(c);
            }
        }

        foreach (var c in _fieldHeaderOnlySelection)
        {
            if (c >= 0 && c < Fields.Count)
            {
                set.Add(c);
            }
        }

        return set.OrderBy(c => c).ToList();
    }

    private List<int> GetSelectedRecordIndices()
    {
        var set = new HashSet<int>();
        foreach (var address in _selectedCells)
        {
            var r = address.RecordIndex;
            if (r >= 0 && r < Records.Count)
            {
                set.Add(r);
            }
        }

        foreach (var r in _recordHeaderOnlySelection)
        {
            if (r >= 0 && r < Records.Count)
            {
                set.Add(r);
            }
        }

        return set.OrderBy(r => r).ToList();
    }

    private void ApplyRecordHeaderDragSelection()
    {
        if (_recordHeaderDragAnchorRecord < 0
            || _recordHeaderDragCurrentRecord < 0
            || Records.Count == 0
            || Fields.Count == 0)
        {
            return;
        }

        ClearHeaderAuxiliarySelectionState();
        _selectedCells.Clear();
        if (_recordHeaderDragIsAdditive || (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _selectedCells.UnionWith(_selectionDragSnapshot);
        }

        var minRecord = Math.Min(_recordHeaderDragAnchorRecord, _recordHeaderDragCurrentRecord);
        var maxRecord = Math.Max(_recordHeaderDragAnchorRecord, _recordHeaderDragCurrentRecord);
        for (var record = minRecord; record <= maxRecord; record++)
        {
            for (var col = 0; col < Fields.Count; col++)
            {
                _selectedCells.Add(new GriddoCellAddress(record, col));
            }
        }
    }

    private void ApplyFieldHeaderDragSelection()
    {
        if (_fieldHeaderDragAnchorField < 0
            || _fieldHeaderDragCurrentField < 0
            || Records.Count == 0
            || Fields.Count == 0)
        {
            return;
        }

        ClearHeaderAuxiliarySelectionState();
        _selectedCells.Clear();
        if (_fieldHeaderDragIsAdditive || (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _selectedCells.UnionWith(_selectionDragSnapshot);
        }

        var minCol = Math.Min(_fieldHeaderDragAnchorField, _fieldHeaderDragCurrentField);
        var maxCol = Math.Max(_fieldHeaderDragAnchorField, _fieldHeaderDragCurrentField);
        for (var col = minCol; col <= maxCol; col++)
        {
            for (var record = 0; record < Records.Count; record++)
            {
                _selectedCells.Add(new GriddoCellAddress(record, col));
            }
        }
    }

    private void SelectProjectedFieldsFromCurrentRecord(GriddoCellAddress current, int clickedField, bool additive)
    {
        if (Records.Count == 0 || Fields.Count == 0)
        {
            return;
        }

        var record = Math.Clamp(current.RecordIndex, 0, Records.Count - 1);
        var currentCol = Math.Clamp(current.FieldIndex, 0, Fields.Count - 1);
        var targetCol = Math.Clamp(clickedField, 0, Fields.Count - 1);

        if (!additive)
        {
            ClearHeaderAuxiliarySelectionState();
            _selectedCells.Clear();
        }

        var selectedFieldsOnRecord = _selectedCells
            .Where(c => c.RecordIndex == record)
            .Select(c => c.FieldIndex)
            .Distinct()
            .ToList();

        // If there is no explicit record selection yet, use current-to-clicked fields on the current record.
        if (selectedFieldsOnRecord.Count == 0)
        {
            var minCol = Math.Min(currentCol, targetCol);
            var maxCol = Math.Max(currentCol, targetCol);
            for (var col = minCol; col <= maxCol; col++)
            {
                selectedFieldsOnRecord.Add(col);
            }
        }

        if (!selectedFieldsOnRecord.Contains(targetCol))
        {
            selectedFieldsOnRecord.Add(targetCol);
        }

        foreach (var col in selectedFieldsOnRecord)
        {
            if (col < 0 || col >= Fields.Count)
            {
                continue;
            }

            for (var r = 0; r < Records.Count; r++)
            {
                _selectedCells.Add(new GriddoCellAddress(r, col));
            }
        }
    }

    private void IncludeRecordsRangeForSelectedFieldsOnRecord(int sourceRecord, int targetRecord)
    {
        if (Records.Count == 0 || Fields.Count == 0)
        {
            return;
        }

        var fromRecord = Math.Clamp(sourceRecord, 0, Records.Count - 1);
        var toRecord = Math.Clamp(targetRecord, 0, Records.Count - 1);
        var minRecord = Math.Min(fromRecord, toRecord);
        var maxRecord = Math.Max(fromRecord, toRecord);

        var selectedFieldsOnRecord = _selectedCells
            .Where(c => c.RecordIndex == fromRecord)
            .Select(c => c.FieldIndex)
            .Distinct()
            .ToList();

        foreach (var col in selectedFieldsOnRecord)
        {
            if (col < 0 || col >= Fields.Count)
            {
                continue;
            }

            for (var r = minRecord; r <= maxRecord; r++)
            {
                _selectedCells.Add(new GriddoCellAddress(r, col));
            }
        }
    }

    private void IncludeFieldsRangeForSelectedRecordsOnField(int sourceField, int targetField)
    {
        if (Records.Count == 0 || Fields.Count == 0)
        {
            return;
        }

        var fromCol = Math.Clamp(sourceField, 0, Fields.Count - 1);
        var toCol = Math.Clamp(targetField, 0, Fields.Count - 1);
        var minCol = Math.Min(fromCol, toCol);
        var maxCol = Math.Max(fromCol, toCol);

        var selectedRecordsOnField = _selectedCells
            .Where(c => c.FieldIndex == fromCol)
            .Select(c => c.RecordIndex)
            .Distinct()
            .ToList();

        foreach (var record in selectedRecordsOnField)
        {
            if (record < 0 || record >= Records.Count)
            {
                continue;
            }

            for (var col = minCol; col <= maxCol; col++)
            {
                _selectedCells.Add(new GriddoCellAddress(record, col));
            }
        }
    }
}
