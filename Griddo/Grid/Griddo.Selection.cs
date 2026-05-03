using System.Windows.Input;
using Griddo.Primitives;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    private void MoveCurrentCell(int rowDelta, int colDelta)
    {
        if (Rows.Count == 0 || Columns.Count == 0)
        {
            return;
        }

        var row = Math.Clamp(_currentCell.RowIndex + rowDelta, 0, Rows.Count - 1);
        var col = Math.Clamp(_currentCell.ColumnIndex + colDelta, 0, Columns.Count - 1);
        _currentCell = new GriddoCellAddress(row, col);
        _selectedCells.Clear();
        _selectedCells.Add(_currentCell);
        InvalidateVisual();
    }

    private bool HandleCellKeyboardNavigation(Key key, bool isCtrlPressed, bool isShiftPressed)
    {
        if (!GriddoCellNavigator.TryGetTarget(key, isCtrlPressed, _currentCell, Rows.Count, Columns.Count, out var target))
        {
            return false;
        }

        if (isShiftPressed)
        {
            if (!_hasKeyboardSelectionAnchor)
            {
                _keyboardSelectionAnchor = _currentCell;
                _hasKeyboardSelectionAnchor = true;
            }

            _currentCell = target;
            SelectRange(_keyboardSelectionAnchor, _currentCell, additive: false);
            InvalidateVisual();
            return true;
        }

        _hasKeyboardSelectionAnchor = false;
        _currentCell = target;
        _selectedCells.Clear();
        _selectedCells.Add(_currentCell);
        InvalidateVisual();
        return true;
    }

    private void ApplyDragSelection()
    {
        _selectedCells.Clear();
        if (_dragIsAdditive || (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _selectedCells.UnionWith(_selectionDragSnapshot);
        }

        var minRow = Math.Min(_dragAnchorCell.RowIndex, _dragCurrentCell.RowIndex);
        var maxRow = Math.Max(_dragAnchorCell.RowIndex, _dragCurrentCell.RowIndex);
        var minCol = Math.Min(_dragAnchorCell.ColumnIndex, _dragCurrentCell.ColumnIndex);
        var maxCol = Math.Max(_dragAnchorCell.ColumnIndex, _dragCurrentCell.ColumnIndex);

        for (var row = minRow; row <= maxRow; row++)
        {
            for (var col = minCol; col <= maxCol; col++)
            {
                _selectedCells.Add(new GriddoCellAddress(row, col));
            }
        }
    }

    private void SelectRange(GriddoCellAddress from, GriddoCellAddress to, bool additive)
    {
        if (!additive)
        {
            _selectedCells.Clear();
        }

        var minRow = Math.Min(from.RowIndex, to.RowIndex);
        var maxRow = Math.Max(from.RowIndex, to.RowIndex);
        var minCol = Math.Min(from.ColumnIndex, to.ColumnIndex);
        var maxCol = Math.Max(from.ColumnIndex, to.ColumnIndex);
        for (var row = minRow; row <= maxRow; row++)
        {
            for (var col = minCol; col <= maxCol; col++)
            {
                if (row >= 0 && row < Rows.Count && col >= 0 && col < Columns.Count)
                {
                    _selectedCells.Add(new GriddoCellAddress(row, col));
                }
            }
        }
    }

    private void SelectColumn(int columnIndex, bool additive)
    {
        if (columnIndex < 0 || columnIndex >= Columns.Count)
        {
            return;
        }

        if (!additive)
        {
            _selectedCells.Clear();
        }

        for (var row = 0; row < Rows.Count; row++)
        {
            _selectedCells.Add(new GriddoCellAddress(row, columnIndex));
        }
    }

    private void SelectAllCells()
    {
        _selectedCells.Clear();
        if (Rows.Count == 0 || Columns.Count == 0)
        {
            return;
        }

        for (var row = 0; row < Rows.Count; row++)
        {
            for (var col = 0; col < Columns.Count; col++)
            {
                _selectedCells.Add(new GriddoCellAddress(row, col));
            }
        }
    }

    private void StopColumnMoveTracking()
    {
        _isTrackingColumnMove = false;
        _isMovingColumn = false;
        _isMovingPointerInColumnHeader = false;
        _columnMoveStartedFromSelectedHeader = false;
        _pendingColumnHeaderSelectionOnMouseUp = false;
        _pendingColumnHeaderIndex = -1;
        _movingColumnIndex = -1;
        _columnMoveCueIndex = -1;
        if (!_isDraggingSelection && !_isResizingColumn && !_isResizingRow && !_isTrackingRowMove && IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }

    private void StopRowMoveTracking()
    {
        _isTrackingRowMove = false;
        _isMovingRow = false;
        _movingRowIndex = -1;
        _rowMoveCueIndex = -1;
        _pendingRowHeaderSelectionOnMouseUp = false;
        _pendingRowHeaderIndex = -1;
        if (!_isDraggingSelection && !_isResizingColumn && !_isResizingRow && !_isTrackingColumnMove && IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }

    private void MoveColumn(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || toIndex < 0 || fromIndex >= Columns.Count || toIndex >= Columns.Count || fromIndex == toIndex)
        {
            return;
        }

        Columns.Move(fromIndex, toIndex);

        var oldCurrent = _currentCell;
        _currentCell = new GriddoCellAddress(oldCurrent.RowIndex, RemapColumnIndex(oldCurrent.ColumnIndex, fromIndex, toIndex));

        var remapped = new HashSet<GriddoCellAddress>();
        foreach (var address in _selectedCells)
        {
            remapped.Add(new GriddoCellAddress(address.RowIndex, RemapColumnIndex(address.ColumnIndex, fromIndex, toIndex)));
        }

        _selectedCells.Clear();
        _selectedCells.UnionWith(remapped);
    }

    private void MoveSelectedColumns(int anchorColumn, int targetColumn)
    {
        var selected = GetSelectedColumnIndices();
        if (selected.Count <= 1 || !selected.Contains(anchorColumn))
        {
            MoveColumn(anchorColumn, targetColumn);
            return;
        }

        var minSelected = selected[0];
        var maxSelected = selected[^1];
        var insertAfterTarget = targetColumn > maxSelected;
        if (targetColumn >= minSelected && targetColumn <= maxSelected)
        {
            return;
        }

        var oldToNew = MoveColumnOrRowIndices(
            Columns.Count,
            selected,
            targetColumn,
            insertAfterTarget,
            index => Columns.Move(index.from, index.to));
        RemapSelectionAfterColumnMove(oldToNew);
    }

    private void MoveSelectedRows(int anchorRow, int targetRow)
    {
        var selected = GetSelectedRowIndices();
        if (selected.Count == 0 || !selected.Contains(anchorRow))
        {
            return;
        }

        var minSelected = selected[0];
        var maxSelected = selected[^1];
        var insertAfterTarget = targetRow > maxSelected;
        if (targetRow >= minSelected && targetRow <= maxSelected)
        {
            return;
        }

        var oldToNew = MoveColumnOrRowIndices(
            Rows.Count,
            selected,
            targetRow,
            insertAfterTarget,
            index => Rows.Move(index.from, index.to));
        RemapSelectionAfterRowMove(oldToNew);
    }

    private static int[] MoveColumnOrRowIndices(
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
        for (var newIndex = 0; newIndex < finalOrder.Count; newIndex++)
        {
            var item = finalOrder[newIndex];
            var currentIndex = currentOrder.IndexOf(item);
            if (currentIndex < 0 || currentIndex == newIndex)
            {
                continue;
            }

            mover((currentIndex, newIndex));
            currentOrder.RemoveAt(currentIndex);
            currentOrder.Insert(newIndex, item);
        }

        var oldToNew = new int[count];
        for (var newIndex = 0; newIndex < finalOrder.Count; newIndex++)
        {
            oldToNew[finalOrder[newIndex]] = newIndex;
        }

        return oldToNew;
    }

    private void RemapSelectionAfterColumnMove(int[] oldToNew)
    {
        if (oldToNew.Length == 0)
        {
            return;
        }

        if (_currentCell.IsValid
            && _currentCell.ColumnIndex >= 0
            && _currentCell.ColumnIndex < oldToNew.Length)
        {
            _currentCell = new GriddoCellAddress(_currentCell.RowIndex, oldToNew[_currentCell.ColumnIndex]);
        }

        var remapped = new HashSet<GriddoCellAddress>();
        foreach (var address in _selectedCells)
        {
            if (address.ColumnIndex >= 0 && address.ColumnIndex < oldToNew.Length)
            {
                remapped.Add(new GriddoCellAddress(address.RowIndex, oldToNew[address.ColumnIndex]));
            }
        }

        _selectedCells.Clear();
        _selectedCells.UnionWith(remapped);
    }

    private void RemapSelectionAfterRowMove(int[] oldToNew)
    {
        if (oldToNew.Length == 0)
        {
            return;
        }

        if (_currentCell.IsValid
            && _currentCell.RowIndex >= 0
            && _currentCell.RowIndex < oldToNew.Length)
        {
            _currentCell = new GriddoCellAddress(oldToNew[_currentCell.RowIndex], _currentCell.ColumnIndex);
        }

        var remapped = new HashSet<GriddoCellAddress>();
        foreach (var address in _selectedCells)
        {
            if (address.RowIndex >= 0 && address.RowIndex < oldToNew.Length)
            {
                remapped.Add(new GriddoCellAddress(oldToNew[address.RowIndex], address.ColumnIndex));
            }
        }

        _selectedCells.Clear();
        _selectedCells.UnionWith(remapped);
    }

    private static int RemapColumnIndex(int columnIndex, int fromIndex, int toIndex)
    {
        if (columnIndex == fromIndex)
        {
            return toIndex;
        }

        if (fromIndex < toIndex)
        {
            return (columnIndex > fromIndex && columnIndex <= toIndex) ? columnIndex - 1 : columnIndex;
        }

        return (columnIndex >= toIndex && columnIndex < fromIndex) ? columnIndex + 1 : columnIndex;
    }


    private void SelectRow(int rowIndex, bool additive)
    {
        if (rowIndex < 0 || rowIndex >= Rows.Count)
        {
            return;
        }

        if (!additive)
        {
            _selectedCells.Clear();
        }

        for (var col = 0; col < Columns.Count; col++)
        {
            _selectedCells.Add(new GriddoCellAddress(rowIndex, col));
        }
    }

    private bool IsRowSelected(int rowIndex)
    {
        return _selectedCells.Any(c => c.RowIndex == rowIndex);
    }

    private bool IsColumnSelected(int columnIndex)
    {
        return _selectedCells.Any(c => c.ColumnIndex == columnIndex);
    }
    private List<int> GetSelectedColumnIndices()
    {
        return _selectedCells
            .Select(c => c.ColumnIndex)
            .Where(c => c >= 0 && c < Columns.Count)
            .Distinct()
            .OrderBy(c => c)
            .ToList();
    }

    private List<int> GetSelectedRowIndices()
    {
        return _selectedCells
            .Select(c => c.RowIndex)
            .Where(r => r >= 0 && r < Rows.Count)
            .Distinct()
            .OrderBy(r => r)
            .ToList();
    }

    private void ApplyRowHeaderDragSelection()
    {
        if (_rowHeaderDragAnchorRow < 0
            || _rowHeaderDragCurrentRow < 0
            || Rows.Count == 0
            || Columns.Count == 0)
        {
            return;
        }

        _selectedCells.Clear();
        if (_rowHeaderDragIsAdditive || (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _selectedCells.UnionWith(_selectionDragSnapshot);
        }

        var minRow = Math.Min(_rowHeaderDragAnchorRow, _rowHeaderDragCurrentRow);
        var maxRow = Math.Max(_rowHeaderDragAnchorRow, _rowHeaderDragCurrentRow);
        for (var row = minRow; row <= maxRow; row++)
        {
            for (var col = 0; col < Columns.Count; col++)
            {
                _selectedCells.Add(new GriddoCellAddress(row, col));
            }
        }
    }

    private void ApplyColumnHeaderDragSelection()
    {
        if (_columnHeaderDragAnchorColumn < 0
            || _columnHeaderDragCurrentColumn < 0
            || Rows.Count == 0
            || Columns.Count == 0)
        {
            return;
        }

        _selectedCells.Clear();
        if (_columnHeaderDragIsAdditive || (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _selectedCells.UnionWith(_selectionDragSnapshot);
        }

        var minCol = Math.Min(_columnHeaderDragAnchorColumn, _columnHeaderDragCurrentColumn);
        var maxCol = Math.Max(_columnHeaderDragAnchorColumn, _columnHeaderDragCurrentColumn);
        for (var col = minCol; col <= maxCol; col++)
        {
            for (var row = 0; row < Rows.Count; row++)
            {
                _selectedCells.Add(new GriddoCellAddress(row, col));
            }
        }
    }

    private void SelectProjectedColumnsFromCurrentRow(GriddoCellAddress current, int clickedColumn, bool additive)
    {
        if (Rows.Count == 0 || Columns.Count == 0)
        {
            return;
        }

        var row = Math.Clamp(current.RowIndex, 0, Rows.Count - 1);
        var currentCol = Math.Clamp(current.ColumnIndex, 0, Columns.Count - 1);
        var targetCol = Math.Clamp(clickedColumn, 0, Columns.Count - 1);

        if (!additive)
        {
            _selectedCells.Clear();
        }

        var selectedColumnsOnRow = _selectedCells
            .Where(c => c.RowIndex == row)
            .Select(c => c.ColumnIndex)
            .Distinct()
            .ToList();

        // If there is no explicit row selection yet, use current-to-clicked columns on the current row.
        if (selectedColumnsOnRow.Count == 0)
        {
            var minCol = Math.Min(currentCol, targetCol);
            var maxCol = Math.Max(currentCol, targetCol);
            for (var col = minCol; col <= maxCol; col++)
            {
                selectedColumnsOnRow.Add(col);
            }
        }

        if (!selectedColumnsOnRow.Contains(targetCol))
        {
            selectedColumnsOnRow.Add(targetCol);
        }

        foreach (var col in selectedColumnsOnRow)
        {
            if (col < 0 || col >= Columns.Count)
            {
                continue;
            }

            for (var r = 0; r < Rows.Count; r++)
            {
                _selectedCells.Add(new GriddoCellAddress(r, col));
            }
        }
    }

    private void IncludeRowsRangeForSelectedColumnsOnRow(int sourceRow, int targetRow)
    {
        if (Rows.Count == 0 || Columns.Count == 0)
        {
            return;
        }

        var fromRow = Math.Clamp(sourceRow, 0, Rows.Count - 1);
        var toRow = Math.Clamp(targetRow, 0, Rows.Count - 1);
        var minRow = Math.Min(fromRow, toRow);
        var maxRow = Math.Max(fromRow, toRow);

        var selectedColumnsOnRow = _selectedCells
            .Where(c => c.RowIndex == fromRow)
            .Select(c => c.ColumnIndex)
            .Distinct()
            .ToList();

        foreach (var col in selectedColumnsOnRow)
        {
            if (col < 0 || col >= Columns.Count)
            {
                continue;
            }

            for (var r = minRow; r <= maxRow; r++)
            {
                _selectedCells.Add(new GriddoCellAddress(r, col));
            }
        }
    }

    private void IncludeColumnsRangeForSelectedRowsOnColumn(int sourceColumn, int targetColumn)
    {
        if (Rows.Count == 0 || Columns.Count == 0)
        {
            return;
        }

        var fromCol = Math.Clamp(sourceColumn, 0, Columns.Count - 1);
        var toCol = Math.Clamp(targetColumn, 0, Columns.Count - 1);
        var minCol = Math.Min(fromCol, toCol);
        var maxCol = Math.Max(fromCol, toCol);

        var selectedRowsOnColumn = _selectedCells
            .Where(c => c.ColumnIndex == fromCol)
            .Select(c => c.RowIndex)
            .Distinct()
            .ToList();

        foreach (var row in selectedRowsOnColumn)
        {
            if (row < 0 || row >= Rows.Count)
            {
                continue;
            }

            for (var col = minCol; col <= maxCol; col++)
            {
                _selectedCells.Add(new GriddoCellAddress(row, col));
            }
        }
    }
}
