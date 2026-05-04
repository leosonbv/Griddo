using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Griddo.Columns;
using Griddo.Editing;
using Griddo.Primitives;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    /// <summary>Minimum pointer travel before column/row move or resize cues activate (DIP).</summary>
    private const double DragCueMinPixels = 1.0;

    // -------------------------------------------------------------------------
    // Mouse down
    // -------------------------------------------------------------------------

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (_hostedDirectRelayDepth > 0)
        {
            e.Handled = true;
            return;
        }

        Focus();
        Keyboard.Focus(this);
        _pendingHostedEditActivation = false;
        _hasKeyboardSelectionAnchor = false;
        var pointer = e.GetPosition(this);
        var modifiers = Keyboard.Modifiers;
        var isCtrlPressed = (modifiers & ModifierKeys.Control) != 0;
        var isShiftPressed = (modifiers & ModifierKeys.Shift) != 0;
        var oldCurrentCell = _currentCell;
        var clickedForEdit = HitTestCell(pointer);

        if (_isEditing)
        {
            if (e.ChangedButton == MouseButton.Left
                && clickedForEdit.IsValid
                && clickedForEdit == _currentCell)
            {
                if (TryGetCurrentInlineDialogButtonRect(out var dialogButtonRect) && dialogButtonRect.Contains(pointer))
                {
                    _ = TriggerInlineDialogButton();
                    InvalidateVisual();
                    CompleteMouseDown(e, handled: true);
                    return;
                }

                var caretIndex = GetCaretIndexFromEditPoint(pointer);
                if (e.ClickCount >= 2)
                {
                    if (TryGetCurrentColumn(out var currentColumn) && currentColumn.Editor is GriddoNumberCellEditor)
                    {
                        _editSession.SelectAll();
                    }
                    else
                    {
                        _editSession.SelectWordAt(caretIndex);
                    }
                }
                else
                {
                    _editSession.SetCaretIndex(caretIndex, extendSelection: isShiftPressed);
                    _isDraggingEditSelection = true;
                    CaptureMouse();
                }

                InvalidateVisual();
                CompleteMouseDown(e, handled: true);
                return;
            }

            if (e.ChangedButton is MouseButton.Left or MouseButton.Right)
            {
                CommitEdit();
            }
        }

        if (e.ChangedButton == MouseButton.Right && HitTestColumnHeader(pointer) is var rightCol and >= 0)
        {
            _headerFocusKind = HeaderFocusKind.Column;
            _headerFocusColumnIndex = rightCol;
            _rowHeaderRightClickOutline.Clear();
            _rowHeaderOnlySelection.Clear();

            var headerAlreadySelected =
                IsColumnHeaderMarkedSelected(rightCol);

            IReadOnlyList<int> contextColumnIndices;
            if (headerAlreadySelected)
            {
                var preserved = new HashSet<int>();
                if (_selectedCells.Count > 0)
                {
                    foreach (var c in GetSelectedColumnIndices())
                    {
                        preserved.Add(c);
                    }
                }
                else
                {
                    preserved.UnionWith(_columnHeaderOnlySelection);
                }

                _columnHeaderOnlySelection.Clear();
                foreach (var c in preserved)
                {
                    _columnHeaderOnlySelection.Add(c);
                }

                _selectedCells.Clear();
                _columnHeaderRightClickOutline.Clear();
                foreach (var c in preserved)
                {
                    _columnHeaderRightClickOutline.Add(c);
                }

                contextColumnIndices = preserved.OrderBy(c => c).ToList();
            }
            else
            {
                ClearHeaderAuxiliarySelectionState();
                _selectedCells.Clear();
                _columnHeaderOnlySelection.Add(rightCol);
                _columnHeaderRightClickOutline.Add(rightCol);
                contextColumnIndices = [rightCol];
                _currentCell = new GriddoCellAddress(
                    Rows.Count == 0 ? 0 : Math.Clamp(_currentCell.RowIndex, 0, Math.Max(0, Rows.Count - 1)),
                    rightCol);
            }

            _hasKeyboardSelectionAnchor = false;
            _isEditing = false;
            ColumnHeaderRightClick?.Invoke(this, new GriddoColumnHeaderMouseEventArgs(rightCol, contextColumnIndices));
            InvalidateVisual();
            CompleteMouseDown(e, handled: true);
            return;
        }

        if (TryBeginDividerResizeOrAutoSize(e, pointer, isCtrlPressed))
        {
            return;
        }

        if (e.ChangedButton == MouseButton.Right && HitTestTopLeftHeaderCell(pointer))
        {
            _headerFocusKind = HeaderFocusKind.Corner;
            ClearHeaderAuxiliarySelectionState();
            _selectedCells.Clear();
            _hasKeyboardSelectionAnchor = false;
            _isEditing = false;
            InvalidateVisual();
            CompleteMouseDown(e, handled: true);
            return;
        }

        var rightRowHeaderHit = HitTestRowHeader(pointer);
        if (e.ChangedButton == MouseButton.Right && rightRowHeaderHit >= 0)
        {
            _headerFocusKind = HeaderFocusKind.Row;
            _headerFocusRowIndex = rightRowHeaderHit;
            _columnHeaderRightClickOutline.Clear();
            _columnHeaderOnlySelection.Clear();

            var rowHeaderAlreadySelected = IsRowHeaderMarkedSelected(rightRowHeaderHit);

            IReadOnlyList<int> contextRowIndices;
            if (rowHeaderAlreadySelected)
            {
                var preserved = new HashSet<int>();
                if (_selectedCells.Count > 0)
                {
                    foreach (var r in GetSelectedRowIndices())
                    {
                        preserved.Add(r);
                    }
                }
                else
                {
                    preserved.UnionWith(_rowHeaderOnlySelection);
                }

                _rowHeaderOnlySelection.Clear();
                foreach (var r in preserved)
                {
                    _rowHeaderOnlySelection.Add(r);
                }

                _selectedCells.Clear();
                _rowHeaderRightClickOutline.Clear();
                foreach (var r in preserved)
                {
                    _rowHeaderRightClickOutline.Add(r);
                }

                contextRowIndices = preserved.OrderBy(r => r).ToList();
            }
            else
            {
                ClearHeaderAuxiliarySelectionState();
                _selectedCells.Clear();
                _rowHeaderOnlySelection.Add(rightRowHeaderHit);
                _rowHeaderRightClickOutline.Add(rightRowHeaderHit);
                contextRowIndices = [rightRowHeaderHit];
                _currentCell = new GriddoCellAddress(
                    rightRowHeaderHit,
                    Columns.Count == 0 ? 0 : Math.Clamp(_currentCell.ColumnIndex, 0, Math.Max(0, Columns.Count - 1)));
            }

            _hasKeyboardSelectionAnchor = false;
            _isEditing = false;
            RowHeaderRightClick?.Invoke(this, new GriddoRowHeaderMouseEventArgs(rightRowHeaderHit, contextRowIndices));
            InvalidateVisual();
            CompleteMouseDown(e, handled: true);
            return;
        }

        if (HitTestTopLeftHeaderCell(pointer))
        {
            SelectAllCells();
            _isEditing = false;
            InvalidateVisual();
            CompleteMouseDown(e, handled: true);
            return;
        }

        var clickedColumnHeader = HitTestColumnHeader(pointer);
        if (clickedColumnHeader >= 0)
        {
            if (e is { ChangedButton: MouseButton.Left, ClickCount: 2 })
            {
                ToggleHeaderSort(clickedColumnHeader, additive: isCtrlPressed);
                CompleteMouseDown(e, handled: true);
                return;
            }

            ClearHeaderFocus();
            var target = new GriddoCellAddress(
                Rows.Count == 0 ? 0 : Math.Clamp(oldCurrentCell.RowIndex, 0, Rows.Count - 1),
                clickedColumnHeader);
            var clickedSelectedColumnHeader = IsColumnHeaderMarkedSelected(clickedColumnHeader);

            if (e.ChangedButton == MouseButton.Left
                && clickedSelectedColumnHeader
                && !isShiftPressed
                && Rows.Count > 0
                && Columns.Count > 0)
            {
                _currentCell = target;
                _isEditing = false;
                InvalidateVisual();
                _isTrackingColumnMove = true;
                _isMovingColumn = false;
                _columnMoveStartedFromSelectedHeader = true;
                _movingColumnIndex = clickedColumnHeader;
                _columnMoveCueIndex = -1;
                _columnMoveStartPoint = pointer;
                _pendingColumnHeaderSelectionOnMouseUp = true;
                _pendingColumnHeaderIndex = clickedColumnHeader;
                _pendingColumnHeaderSelectionAdditive = isCtrlPressed;
                _pendingColumnHeaderPreserveSelection = clickedSelectedColumnHeader && !isCtrlPressed;
                CaptureMouse();
                CompleteMouseDown(e, handled: true);
                return;
            }

            if (isShiftPressed && oldCurrentCell.IsValid && Rows.Count > 0 && Columns.Count > 0)
            {
                SelectRange(oldCurrentCell, target, isCtrlPressed);
                IncludeColumnsRangeForSelectedRowsOnColumn(oldCurrentCell.ColumnIndex, clickedColumnHeader);
            }
            else
            {
                SelectColumn(clickedColumnHeader, isCtrlPressed);
            }

            _currentCell = target;
            _isEditing = false;
            InvalidateVisual();
            if (!isShiftPressed
                && e.ChangedButton == MouseButton.Left
                && Rows.Count > 0
                && Columns.Count > 0)
            {
                _columnHeaderDragIsAdditive = isCtrlPressed;
                _selectionDragSnapshot.Clear();
                _selectionDragSnapshot.UnionWith(_selectedCells);
                _columnHeaderDragAnchorColumn = clickedColumnHeader;
                _columnHeaderDragCurrentColumn = clickedColumnHeader;
                _isDraggingColumnHeaderSelection = true;
                CaptureMouse();
            }

            CompleteMouseDown(e, handled: true);
            return;
        }

        var clickedRowHeader = HitTestRowHeader(pointer);
        if (clickedRowHeader >= 0)
        {
            ClearHeaderFocus();
            var target = new GriddoCellAddress(
                clickedRowHeader,
                Columns.Count == 0 ? 0 : Math.Clamp(oldCurrentCell.ColumnIndex, 0, Columns.Count - 1));
            var clickedSelectedRowHeader = IsRowHeaderMarkedSelected(clickedRowHeader);

            if (e.ChangedButton == MouseButton.Left
                && clickedSelectedRowHeader
                && !isShiftPressed
                && Rows.Count > 0
                && Columns.Count > 0)
            {
                _currentCell = target;
                _isEditing = false;
                InvalidateVisual();
                _isTrackingRowMove = true;
                _isMovingRow = false;
                _movingRowIndex = clickedRowHeader;
                _rowMoveCueIndex = clickedRowHeader;
                _rowMoveStartPoint = pointer;
                _pendingRowHeaderSelectionOnMouseUp = true;
                _pendingRowHeaderIndex = clickedRowHeader;
                _pendingRowHeaderSelectionAdditive = isCtrlPressed;
                _pendingRowHeaderPreserveSelection = clickedSelectedRowHeader && !isCtrlPressed;
                CaptureMouse();
                CompleteMouseDown(e, handled: true);
                return;
            }

            if (isShiftPressed && oldCurrentCell.IsValid && Rows.Count > 0 && Columns.Count > 0)
            {
                SelectRange(oldCurrentCell, target, isCtrlPressed);
                IncludeRowsRangeForSelectedColumnsOnRow(oldCurrentCell.RowIndex, clickedRowHeader);
            }
            else
            {
                SelectRow(clickedRowHeader, isCtrlPressed);
            }

            if (!isShiftPressed
                && e.ChangedButton == MouseButton.Left
                && Rows.Count > 0
                && Columns.Count > 0)
            {
                _rowHeaderDragIsAdditive = isCtrlPressed;
                _selectionDragSnapshot.Clear();
                _selectionDragSnapshot.UnionWith(_selectedCells);
                _rowHeaderDragAnchorRow = clickedRowHeader;
                _rowHeaderDragCurrentRow = clickedRowHeader;
                _isDraggingRowHeaderSelection = true;
                CaptureMouse();
            }

            _currentCell = target;
            _isEditing = false;
            InvalidateVisual();
            CompleteMouseDown(e, handled: true);
            return;
        }

        var clicked = HitTestCell(pointer);
        if (!clicked.IsValid)
        {
            CompleteMouseDown(e, handled: false);
            return;
        }

        ClearHeaderFocus();
        if (e.ChangedButton == MouseButton.Right)
        {
            ClearHeaderAuxiliarySelectionState();
        }

        if (e.ChangedButton == MouseButton.Right
            && HostedPlotDirectEditOnMouseDown
            && e.ClickCount == 1
            && Columns[clicked.ColumnIndex] is IGriddoHostedColumnView hostedRightDirect)
        {
            var wasSelectedHosted = _selectedCells.Contains(clicked);
            if (!wasSelectedHosted)
            {
                _selectedCells.Clear();
                _selectedCells.Add(clicked);
            }
            _currentCell = clicked;
            _isDraggingSelection = false;
            _pendingHostedEditActivation = false;
            SyncHostedCells();
            SetCurrentHostedCellEditMode(true);
            if (TryGetHostedElement(clicked) is { } hostForRightRelay)
            {
                UpdateLayout();
                hostForRightRelay.UpdateLayout();
                _hostedDirectRelayDepth++;
                try
                {
                    hostedRightDirect.RelayDirectEditMouseDown(hostForRightRelay, e);
                }
                finally
                {
                    _hostedDirectRelayDepth--;
                }
            }

            _isEditing = false;
            InvalidateVisual();
            CompleteMouseDown(e, handled: true);
            return;
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            var wasSelected = _selectedCells.Contains(clicked);
            if (!wasSelected)
            {
                _selectedCells.Clear();
                _selectedCells.Add(clicked);
            }
            _currentCell = clicked;

            _isEditing = false;
            InvalidateVisual();
            OpenCellContextMenu(e, clicked, wasSelected);
            CompleteMouseDown(e, handled: true);
            return;
        }

        if (e.ChangedButton == MouseButton.Left
            && !isShiftPressed
            && !isCtrlPressed)
        {
            if (e.ClickCount == 2)
            {
                _selectedCells.Clear();
                _selectedCells.Add(clicked);
                _currentCell = clicked;
                _isDraggingSelection = false;
                if (IsCheckboxToggleCell(clicked))
                {
                    ToggleBoolCell(clicked);
                }
                else if (Columns[clicked.ColumnIndex] is not IGriddoHostedColumnView)
                {
                    BeginEditWithoutReplacing();
                }

                InvalidateVisual();
                CompleteMouseDown(e, handled: true);
                return;
            }

            if (HostedPlotDirectEditOnMouseDown
                && e.ClickCount == 1
                && Columns[clicked.ColumnIndex] is IGriddoHostedColumnView hostedDirect)
            {
                if (TryGetHostedElement(clicked) is { } hostedForDirect
                    && hostedDirect.IsHostInEditMode(hostedForDirect))
                {
                    InvalidateVisual();
                    CompleteMouseDown(e, handled: false);
                    return;
                }

                _selectedCells.Clear();
                _selectedCells.Add(clicked);
                _currentCell = clicked;
                _isDraggingSelection = false;
                _pendingHostedEditActivation = false;
                SyncHostedCells();
                SetCurrentHostedCellEditMode(true);
                if (TryGetHostedElement(clicked) is { } hostForRelay)
                {
                    UpdateLayout();
                    hostForRelay.UpdateLayout();
                    _hostedDirectRelayDepth++;
                    try
                    {
                        hostedDirect.RelayDirectEditMouseDown(hostForRelay, e);
                    }
                    finally
                    {
                        _hostedDirectRelayDepth--;
                    }
                }

                InvalidateVisual();
                CompleteMouseDown(e, handled: true);
                return;
            }

            if (ImmediateCellEditOnSingleClick
                && e.ClickCount == 1
                && Columns[clicked.ColumnIndex] is not IGriddoHostedColumnView
                && !IsCheckboxToggleCell(clicked))
            {
                _selectedCells.Clear();
                _selectedCells.Add(clicked);
                _currentCell = clicked;
                _isDraggingSelection = false;
                BeginEditWithoutReplacing();
                InvalidateVisual();
                CompleteMouseDown(e, handled: true);
                return;
            }

            if (e.ClickCount == 1
                && oldCurrentCell.IsValid
                && clicked == oldCurrentCell)
            {
                if (Columns[clicked.ColumnIndex] is IGriddoHostedColumnView hostedSameCell)
                {
                    if (TryGetHostedElement(clicked) is { } hostedSameElement
                        && hostedSameCell.IsHostInEditMode(hostedSameElement))
                    {
                        InvalidateVisual();
                        CompleteMouseDown(e, handled: false);
                        return;
                    }

                    _pendingHostedEditActivation = true;
                    _pendingHostedEditCell = clicked;
                    _dragAnchorCell = clicked;
                    _dragCurrentCell = clicked;
                    _isDraggingSelection = true;
                    CaptureMouse();
                    InvalidateVisual();
                    CompleteMouseDown(e, handled: true);
                    return;
                }
                else if (IsCheckboxToggleCell(clicked))
                {
                    _selectedCells.Clear();
                    _selectedCells.Add(clicked);
                    _currentCell = clicked;
                    _isDraggingSelection = false;
                    ToggleBoolCell(clicked);
                    InvalidateVisual();
                    CompleteMouseDown(e, handled: true);
                    return;
                }
                else
                {
                    _selectedCells.Clear();
                    _selectedCells.Add(clicked);
                    _currentCell = clicked;
                    _isDraggingSelection = false;
                    BeginEditWithoutReplacing();
                    InvalidateVisual();
                    CompleteMouseDown(e, handled: true);
                    return;
                }
            }
        }

        if (e.ChangedButton == MouseButton.Left
            && isShiftPressed
            && oldCurrentCell.IsValid
            && Rows.Count > 0
            && Columns.Count > 0)
        {
            SelectRange(oldCurrentCell, clicked, isCtrlPressed);
            _currentCell = clicked;
            _isEditing = false;
            InvalidateVisual();
            CompleteMouseDown(e, handled: true);
            return;
        }

        if (e.ChangedButton == MouseButton.Left
            && Columns[clicked.ColumnIndex] is IGriddoHostedColumnView hostedForEdit
            && TryGetHostedElement(clicked) is { } hostedElement
            && hostedForEdit.IsHostInEditMode(hostedElement))
        {
            if (isCtrlPressed)
            {
                _selectedCells.Add(clicked);
            }
            else
            {
                _selectedCells.Clear();
                _selectedCells.Add(clicked);
            }

            _currentCell = clicked;
            _isDraggingSelection = false;
            _isEditing = false;
            InvalidateVisual();
            CompleteMouseDown(e, handled: false);
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            CompleteMouseDown(e, handled: false);
            return;
        }

        _dragIsAdditive = isCtrlPressed;

        if (_dragIsAdditive)
        {
            _selectedCells.Add(clicked);
        }
        else
        {
            _selectedCells.Clear();
            _selectedCells.Add(clicked);
        }

        _selectionDragSnapshot.Clear();
        _selectionDragSnapshot.UnionWith(_selectedCells);
        _dragAnchorCell = clicked;
        _dragCurrentCell = clicked;
        _isDraggingSelection = true;
        CaptureMouse();

        _currentCell = clicked;
        _isEditing = false;
        InvalidateVisual();
        CompleteMouseDown(e, handled: true);
    }

    private void OpenCellContextMenu(MouseButtonEventArgs e, GriddoCellAddress cell, bool cellWasAlreadySelected)
    {
        var pos = e.GetPosition(this);
        var args = new GriddoCellContextMenuEventArgs(cell, pos, cellWasAlreadySelected);
        CellContextMenuOpening?.Invoke(this, args);
        if (args.Handled || CellContextMenu is null)
        {
            return;
        }

        CellContextMenu.PlacementTarget = this;
        CellContextMenu.Placement = PlacementMode.RelativePoint;
        CellContextMenu.HorizontalOffset = pos.X;
        CellContextMenu.VerticalOffset = pos.Y;
        CellContextMenu.IsOpen = true;
    }

    private void CompleteMouseDown(MouseButtonEventArgs e, bool handled)
    {
        if (handled)
        {
            e.Handled = true;
        }

        base.OnMouseDown(e);
    }

    /// <summary>Column or row divider: double-click autosize, drag starts resize with capture.</summary>
    private bool TryBeginDividerResizeOrAutoSize(MouseButtonEventArgs e, Point pointer, bool isCtrlPressed)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return false;
        }

        var dividerColumn = HitTestColumnDivider(pointer);
        if (dividerColumn >= 0)
        {
            if (e.ClickCount == 2)
            {
                if (isCtrlPressed)
                {
                    AutoSizeAllColumns();
                    e.Handled = true;
                    return true;
                }

                AutoSizeColumn(dividerColumn);
                e.Handled = true;
                return true;
            }

            _isResizingColumn = true;
            _resizingColumnIndex = dividerColumn;
            _resizeStartPoint = pointer;
            _resizeInitialSize = GetColumnWidth(dividerColumn);
            CaptureMouse();
            e.Handled = true;
            return true;
        }

        var dividerRow = HitTestRowDivider(pointer);
        if (dividerRow < 0)
        {
            return false;
        }

        if (e.ClickCount == 2)
        {
            if (isCtrlPressed)
            {
                AutoSizeAllColumns();
                e.Handled = true;
                return true;
            }

            AutoSizeRow(dividerRow);
            e.Handled = true;
            return true;
        }

        _isResizingRow = true;
        _resizingRowIndex = dividerRow;
        ExitFillRowsUsingCurrentDisplayedRowHeight();
        _resizePreserveOldRowHeight = GetRowHeight(dividerRow);
        _resizePreserveOldVerticalOffset = _verticalOffset;
        _resizePreserveOldHorizontalOffset = _horizontalOffset;
        _resizeStartPoint = pointer;
        _resizeInitialSize = GetRowHeight(dividerRow);
        CaptureMouse();
        e.Handled = true;
        return true;
    }

    // -------------------------------------------------------------------------
    // Preview mouse down (hosted columns)
    // -------------------------------------------------------------------------

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            ClearHeaderAuxiliarySelectionState();
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            base.OnPreviewMouseDown(e);
            return;
        }

        var pointer = e.GetPosition(this);
        var clicked = HitTestCell(pointer);
        if (!clicked.IsValid || Columns[clicked.ColumnIndex] is not IGriddoHostedColumnView)
        {
            base.OnPreviewMouseDown(e);
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var isCtrlPressed = (modifiers & ModifierKeys.Control) != 0;
        var isShiftPressed = (modifiers & ModifierKeys.Shift) != 0;
        var oldCurrentCell = _currentCell;

        if (!isShiftPressed && !isCtrlPressed)
        {
            if (e.ClickCount == 2)
            {
                _selectedCells.Clear();
                _selectedCells.Add(clicked);
                _currentCell = clicked;
                _isDraggingSelection = false;
                _isEditing = false;
                InvalidateVisual();
                base.OnPreviewMouseDown(e);
                return;
            }

            if (e.ClickCount == 1 && oldCurrentCell.IsValid && clicked == oldCurrentCell)
            {
                _selectedCells.Clear();
                _selectedCells.Add(clicked);
                _currentCell = clicked;
                _isDraggingSelection = false;
                _isEditing = false;
                InvalidateVisual();
                base.OnPreviewMouseDown(e);
                return;
            }
        }

        if (isShiftPressed && oldCurrentCell.IsValid && Rows.Count > 0 && Columns.Count > 0)
        {
            SelectRange(oldCurrentCell, clicked, isCtrlPressed);
            _currentCell = clicked;
            _isEditing = false;
            InvalidateVisual();
            base.OnPreviewMouseDown(e);
            return;
        }

        _dragIsAdditive = isCtrlPressed;

        if (_dragIsAdditive)
        {
            _selectedCells.Add(clicked);
        }
        else
        {
            _selectedCells.Clear();
            _selectedCells.Add(clicked);
        }

        _selectionDragSnapshot.Clear();
        _selectionDragSnapshot.UnionWith(_selectedCells);
        _dragAnchorCell = clicked;
        _dragCurrentCell = clicked;
        _isDraggingSelection = false;
        _currentCell = clicked;
        _isEditing = false;
        InvalidateVisual();
        base.OnPreviewMouseDown(e);
    }

    // -------------------------------------------------------------------------
    // Mouse move
    // -------------------------------------------------------------------------

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pointer = e.GetPosition(this);
        UpdateColumnHeaderTooltip(pointer);
        if (_isDraggingEditSelection && e.LeftButton == MouseButtonState.Pressed)
        {
            var caretIndex = GetCaretIndexFromEditPoint(pointer);
            _editSession.SetCaretIndex(caretIndex, extendSelection: true);
            InvalidateVisual();
            e.Handled = true;
            base.OnMouseMove(e);
            return;
        }

        if (_isResizingColumn)
        {
            var delta = IsBodyTransposed ? pointer.Y - _resizeStartPoint.Y : pointer.X - _resizeStartPoint.X;
            SetColumnWidth(_resizingColumnIndex, _resizeInitialSize + delta);
            InvalidateVisual();
            e.Handled = true;
            base.OnMouseMove(e);
            return;
        }

        if (_isResizingRow)
        {
            double requestedHeight;
            if (IsBodyTransposed)
            {
                var bodyPx = pointer.X - _rowHeaderWidth;
                requestedHeight = GetUniformRowHeightScreenFromDividerBodyX(_resizingRowIndex, bodyPx);
            }
            else
            {
                var bodyPy = pointer.Y - ScaledColumnHeaderHeight;
                requestedHeight = GetUniformRowHeightScreenFromDividerBodyY(_resizingRowIndex, bodyPy);
            }

            SetRowHeightKeepingRowTop(_resizingRowIndex, requestedHeight);
            InvalidateVisual();
            e.Handled = true;
            base.OnMouseMove(e);
            return;
        }

        if (_isTrackingColumnMove)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                StopColumnMoveTracking();
                base.OnMouseMove(e);
                return;
            }

            var dragDistance = (pointer - _columnMoveStartPoint).Length;
            var isPointerInColumnHeader = HitTestColumnHeader(pointer) >= 0;
            var shouldShowMovingHeaderCue = isPointerInColumnHeader && dragDistance >= DragCueMinPixels;
            if (_isMovingPointerInColumnHeader != shouldShowMovingHeaderCue)
            {
                _isMovingPointerInColumnHeader = shouldShowMovingHeaderCue;
                InvalidateVisual();
            }

            if (!_isMovingColumn)
            {
                if (dragDistance >= DragCueMinPixels)
                {
                    _isMovingColumn = true;
                }
            }

            if (_isMovingColumn)
            {
                AutoScrollDuringColumnMove(pointer.X);
                var targetColumn = HitTestColumnHeaderDrag(pointer);
                if (targetColumn >= 0 && targetColumn != _movingColumnIndex)
                {
                    _columnMoveCueIndex = targetColumn;
                    InvalidateVisual();
                }
                else if (_columnMoveCueIndex != -1)
                {
                    _columnMoveCueIndex = -1;
                    InvalidateVisual();
                }
            }

            e.Handled = true;
            base.OnMouseMove(e);
            return;
        }

        if (_isTrackingRowMove)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                StopRowMoveTracking();
                base.OnMouseMove(e);
                return;
            }

            var dragDistance = (pointer - _rowMoveStartPoint).Length;
            if (!_isMovingRow && dragDistance >= DragCueMinPixels)
            {
                _isMovingRow = true;
            }

            AutoScrollDuringRowInteraction(pointer.Y);
            var targetRow = HitTestRowHeaderDrag(pointer);
            if (targetRow >= 0 && targetRow != _rowMoveCueIndex)
            {
                _rowMoveCueIndex = targetRow;
                InvalidateVisual();
            }

            e.Handled = true;
            base.OnMouseMove(e);
            return;
        }

        if (_isDraggingColumnHeaderSelection
            && IsMouseCaptured
            && e.LeftButton == MouseButtonState.Pressed)
        {
            AutoScrollDuringColumnMove(pointer.X);
            var hoveredColumnHeader = HitTestColumnHeaderDrag(pointer);
            if (hoveredColumnHeader >= 0 && hoveredColumnHeader != _columnHeaderDragCurrentColumn)
            {
                _columnHeaderDragCurrentColumn = hoveredColumnHeader;
                ApplyColumnHeaderDragSelection();
                InvalidateVisual();
                e.Handled = true;
            }

            base.OnMouseMove(e);
            return;
        }

        if (_isDraggingRowHeaderSelection
            && IsMouseCaptured
            && e.LeftButton == MouseButtonState.Pressed)
        {
            AutoScrollDuringRowInteraction(pointer.Y);
            var hoveredRowHeader = HitTestRowHeaderDrag(pointer);
            if (hoveredRowHeader >= 0 && hoveredRowHeader != _rowHeaderDragCurrentRow)
            {
                _rowHeaderDragCurrentRow = hoveredRowHeader;
                ApplyRowHeaderDragSelection();
                InvalidateVisual();
                e.Handled = true;
            }

            base.OnMouseMove(e);
            return;
        }

        if (!_isDraggingSelection
            || !IsMouseCaptured
            || e.LeftButton != MouseButtonState.Pressed)
        {
            UpdateResizeCursor(pointer);
            base.OnMouseMove(e);
            return;
        }

        AutoScrollDuringCellSelection(pointer);
        var hovered = HitTestCell(pointer);
        if (!hovered.IsValid)
        {
            base.OnMouseMove(e);
            return;
        }

        if (_dragCurrentCell == hovered)
        {
            base.OnMouseMove(e);
            return;
        }

        _dragCurrentCell = hovered;
        ApplyDragSelection();
        InvalidateVisual();
        e.Handled = true;
        base.OnMouseMove(e);
    }

    protected override void OnToolTipOpening(ToolTipEventArgs e)
    {
        UpdateColumnHeaderTooltip(Mouse.GetPosition(this));
        if (ReferenceEquals(ToolTip, _columnHeaderToolTip)
            && IsEmptyColumnHeaderToolTipContent(_columnHeaderToolTip.Content))
        {
            e.Handled = true;
        }

        base.OnToolTipOpening(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        // Pointer left the grid; allow the next header hover to run a fresh ToolTip attach cycle.
        // Do not reset while the column-header tooltip is open — the pointer often leaves the grid to read the popup.
        if (ReferenceEquals(ToolTip, _columnHeaderToolTip) && !_columnHeaderToolTip.IsOpen)
        {
            ClearColumnHeaderToolTipContent();
            _columnHeaderToolTipNeedsReattach = true;
            _priorPointerOnDescribedColumnHeader = false;
        }

        base.OnMouseLeave(e);
    }

    private void ColumnHeaderToolTipOnClosed(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_columnHeaderToolTipClosedSuppress > 0)
        {
            return;
        }

        _columnHeaderToolTipNeedsReattach = true;
        _priorPointerOnDescribedColumnHeader = false;
        if (ReferenceEquals(ToolTip, _columnHeaderToolTip))
        {
            _columnHeaderToolTip.Content = null;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, RefreshColumnHeaderTooltipIfApplicable);
    }

    private void RefreshColumnHeaderTooltipIfApplicable()
    {
        if (!IsLoaded || !IsVisible || !ReferenceEquals(ToolTip, _columnHeaderToolTip))
        {
            return;
        }

        UpdateColumnHeaderTooltip(Mouse.GetPosition(this));
    }

    private void UpdateColumnHeaderTooltip(Point pointer)
    {
        if (!ReferenceEquals(ToolTip, _columnHeaderToolTip))
        {
            return;
        }

        var headerCol = HitTestColumnHeader(pointer);
        var inStrip = headerCol >= 0 && headerCol < Columns.Count;
        if (inStrip && TryGetColumnHeaderDescription(Columns[headerCol], out var text))
        {
            // Reattach when (a) tooltip service asked for it after close, or (b) pointer re-enters a described
            // header from body / undescribed header — WPF will not reopen on the same attach after leaving the strip
            // if we only rely on ToolTip.Closed (it may not run when IsOpen is cleared from mouse moves).
            if (!_priorPointerOnDescribedColumnHeader || _columnHeaderToolTipNeedsReattach)
            {
                _columnHeaderToolTipClosedSuppress++;
                try
                {
                    ToolTip = null;
                    ToolTip = _columnHeaderToolTip;
                }
                finally
                {
                    _columnHeaderToolTipClosedSuppress--;
                }

                _columnHeaderToolTipNeedsReattach = false;
            }

            _priorPointerOnDescribedColumnHeader = true;
            ApplyColumnHeaderToolTipText(text);
            return;
        }

        _priorPointerOnDescribedColumnHeader = false;

        if (inStrip)
        {
            if (_columnHeaderToolTip.IsOpen)
            {
                _columnHeaderToolTip.IsOpen = false;
            }

            ClearColumnHeaderToolTipContent();
            _columnHeaderToolTipNeedsReattach = true;
            return;
        }

        if (_columnHeaderToolTip.IsOpen)
        {
            _columnHeaderToolTip.IsOpen = false;
        }

        ClearColumnHeaderToolTipContent();
        _columnHeaderToolTipNeedsReattach = true;
    }

    private void ApplyColumnHeaderToolTipText(string text)
    {
        if (_columnHeaderToolTip.Content is TextBlock tb)
        {
            tb.Text = text;
            return;
        }

        _columnHeaderToolTip.Content = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 480,
            Focusable = false,
            Foreground = SystemColors.InfoTextBrush
        };
    }

    private void ClearColumnHeaderToolTipContent()
    {
        _columnHeaderToolTip.Content = null;
    }

    private static bool IsEmptyColumnHeaderToolTipContent(object? content) =>
        content switch
        {
            null => true,
            string s => string.IsNullOrWhiteSpace(s),
            TextBlock tb => string.IsNullOrWhiteSpace(tb.Text),
            _ => false
        };

    private static bool TryGetColumnHeaderDescription(IGriddoColumnView column, out string text)
    {
        if (column is IGriddoColumnDescriptionView descriptionView
            && !string.IsNullOrWhiteSpace(descriptionView.Description))
        {
            text = descriptionView.Description.Trim();
            return true;
        }

        text = string.Empty;
        return false;
    }

    // -------------------------------------------------------------------------
    // Auto-scroll while dragging near viewport edges
    // -------------------------------------------------------------------------

    private void AutoScrollDuringColumnMove(double pointerX)
    {
        if (Columns.Count == 0 || _viewportBodyWidth <= 0)
        {
            return;
        }

        var scrollStart = _rowHeaderWidth + GetFixedColumnsWidth();
        var scrollEnd = _rowHeaderWidth + _viewportBodyWidth;
        if (scrollEnd <= scrollStart)
        {
            return;
        }

        const double edgeBand = 24.0;
        const double maxSpeedPerMove = 48.0;
        var delta = 0.0;
        if (pointerX < scrollStart + edgeBand)
        {
            var pressure = (scrollStart + edgeBand - pointerX) / edgeBand;
            delta = -Math.Clamp(pressure, 0, 3) * maxSpeedPerMove;
        }
        else if (pointerX > scrollEnd - edgeBand)
        {
            var pressure = (pointerX - (scrollEnd - edgeBand)) / edgeBand;
            delta = Math.Clamp(pressure, 0, 3) * maxSpeedPerMove;
        }

        if (Math.Abs(delta) < double.Epsilon)
        {
            return;
        }

        var oldOffset = _horizontalOffset;
        SetHorizontalOffset(_horizontalOffset + delta);
        if (Math.Abs(oldOffset - _horizontalOffset) > double.Epsilon)
        {
            InvalidateVisual();
        }
    }

    private void AutoScrollDuringRowInteraction(double pointerY)
    {
        if (Rows.Count == 0 || _viewportBodyHeight <= 0)
        {
            return;
        }

        var scrollStartY = ScaledColumnHeaderHeight;
        var scrollEndY = ScaledColumnHeaderHeight + _viewportBodyHeight;
        if (scrollEndY <= scrollStartY)
        {
            return;
        }

        const double edgeBand = 24.0;
        const double maxSpeedPerMove = 36.0;
        var delta = 0.0;
        if (pointerY < scrollStartY + edgeBand)
        {
            var pressure = (scrollStartY + edgeBand - pointerY) / edgeBand;
            delta = -Math.Clamp(pressure, 0, 3) * maxSpeedPerMove;
        }
        else if (pointerY > scrollEndY - edgeBand)
        {
            var pressure = (pointerY - (scrollEndY - edgeBand)) / edgeBand;
            delta = Math.Clamp(pressure, 0, 3) * maxSpeedPerMove;
        }

        if (Math.Abs(delta) < double.Epsilon)
        {
            return;
        }

        var oldOffset = _verticalOffset;
        SetVerticalOffset(_verticalOffset + delta);
        if (Math.Abs(oldOffset - _verticalOffset) > double.Epsilon)
        {
            InvalidateVisual();
        }
    }

    private void AutoScrollDuringCellSelection(Point pointer)
    {
        if (Rows.Count == 0 || Columns.Count == 0 || _viewportBodyWidth <= 0 || _viewportBodyHeight <= 0)
        {
            return;
        }

        const double edgeBand = 24.0;
        const double maxHorizontalSpeed = 48.0;
        const double maxVerticalSpeed = 36.0;

        var scrollStartX = _rowHeaderWidth + GetFixedColumnsWidth();
        var scrollEndX = _rowHeaderWidth + _viewportBodyWidth;
        var horizontalDelta = 0.0;
        if (scrollEndX > scrollStartX)
        {
            if (pointer.X < scrollStartX + edgeBand)
            {
                var pressure = (scrollStartX + edgeBand - pointer.X) / edgeBand;
                horizontalDelta = -Math.Clamp(pressure, 0, 3) * maxHorizontalSpeed;
            }
            else if (pointer.X > scrollEndX - edgeBand)
            {
                var pressure = (pointer.X - (scrollEndX - edgeBand)) / edgeBand;
                horizontalDelta = Math.Clamp(pressure, 0, 3) * maxHorizontalSpeed;
            }
        }

        var scrollStartY = ScaledColumnHeaderHeight;
        var scrollEndY = ScaledColumnHeaderHeight + _viewportBodyHeight;
        var verticalDelta = 0.0;
        if (pointer.Y < scrollStartY + edgeBand)
        {
            var pressure = (scrollStartY + edgeBand - pointer.Y) / edgeBand;
            verticalDelta = -Math.Clamp(pressure, 0, 3) * maxVerticalSpeed;
        }
        else if (pointer.Y > scrollEndY - edgeBand)
        {
            var pressure = (pointer.Y - (scrollEndY - edgeBand)) / edgeBand;
            verticalDelta = Math.Clamp(pressure, 0, 3) * maxVerticalSpeed;
        }

        var oldH = _horizontalOffset;
        var oldV = _verticalOffset;
        if (Math.Abs(horizontalDelta) > double.Epsilon)
        {
            SetHorizontalOffset(_horizontalOffset + horizontalDelta);
        }

        if (Math.Abs(verticalDelta) > double.Epsilon)
        {
            SetVerticalOffset(_verticalOffset + verticalDelta);
        }

        if (Math.Abs(oldH - _horizontalOffset) > double.Epsilon || Math.Abs(oldV - _verticalOffset) > double.Epsilon)
        {
            InvalidateVisual();
        }
    }

    // -------------------------------------------------------------------------
    // Mouse up
    // -------------------------------------------------------------------------

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_isDraggingEditSelection && e.ChangedButton == MouseButton.Left)
        {
            _isDraggingEditSelection = false;
            if (!_isDraggingSelection
                && !_isDraggingColumnHeaderSelection
                && !_isDraggingRowHeaderSelection
                && !_isResizingColumn
                && !_isResizingRow
                && !_isTrackingColumnMove
                && !_isTrackingRowMove
                && IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
        }

        if (_isTrackingColumnMove && e.ChangedButton == MouseButton.Left)
        {
            if (_isMovingColumn &&
                _movingColumnIndex >= 0 &&
                _columnMoveCueIndex >= 0 &&
                _movingColumnIndex != _columnMoveCueIndex)
            {
                if (_columnMoveStartedFromSelectedHeader)
                {
                    MoveSelectedColumns(_movingColumnIndex, _columnMoveCueIndex);
                }
                else
                {
                    MoveColumn(_movingColumnIndex, _columnMoveCueIndex);
                }
                InvalidateVisual();
            }
            else if (_pendingColumnHeaderSelectionOnMouseUp
                && _pendingColumnHeaderIndex >= 0)
            {
                if (!_pendingColumnHeaderPreserveSelection)
                {
                    SelectColumn(_pendingColumnHeaderIndex, _pendingColumnHeaderSelectionAdditive);
                }

                _currentCell = new GriddoCellAddress(
                    Rows.Count == 0 ? 0 : Math.Clamp(_currentCell.RowIndex, 0, Rows.Count - 1),
                    _pendingColumnHeaderIndex);
                _isEditing = false;
                InvalidateVisual();
            }

            StopColumnMoveTracking();
            InvalidateVisual();
            e.Handled = true;
        }

        if (_isTrackingRowMove && e.ChangedButton == MouseButton.Left)
        {
            if (_isMovingRow &&
                _movingRowIndex >= 0 &&
                _rowMoveCueIndex >= 0 &&
                _movingRowIndex != _rowMoveCueIndex)
            {
                MoveSelectedRows(_movingRowIndex, _rowMoveCueIndex);
                InvalidateVisual();
            }
            else if (_pendingRowHeaderSelectionOnMouseUp
                && _pendingRowHeaderIndex >= 0)
            {
                if (!_pendingRowHeaderPreserveSelection)
                {
                    SelectRow(_pendingRowHeaderIndex, _pendingRowHeaderSelectionAdditive);
                }

                _currentCell = new GriddoCellAddress(
                    _pendingRowHeaderIndex,
                    Columns.Count == 0 ? 0 : Math.Clamp(_currentCell.ColumnIndex, 0, Columns.Count - 1));
                _isEditing = false;
                InvalidateVisual();
            }

            StopRowMoveTracking();
            e.Handled = true;
        }

        if (_isResizingColumn && e.ChangedButton == MouseButton.Left)
        {
            _isResizingColumn = false;
            _resizingColumnIndex = -1;
            if (!_isDraggingSelection && IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            e.Handled = true;
        }

        if (_isResizingRow && e.ChangedButton == MouseButton.Left)
        {
            _isResizingRow = false;
            var savedDivider = _resizingRowIndex;
            _resizingRowIndex = -1;
            if (savedDivider >= 0)
            {
                ApplyInteractiveRowResizeScrollPreservation(
                    savedDivider,
                    _resizePreserveOldRowHeight,
                    IsBodyTransposed ? _resizePreserveOldHorizontalOffset : _resizePreserveOldVerticalOffset);
            }

            if (!_isDraggingSelection && IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            e.Handled = true;
        }

        if (_isDraggingSelection && e.ChangedButton == MouseButton.Left)
        {
            var shouldActivateHostedEdit =
                _pendingHostedEditActivation
                && _pendingHostedEditCell.IsValid
                && _dragAnchorCell == _pendingHostedEditCell
                && _dragCurrentCell == _pendingHostedEditCell
                && _currentCell == _pendingHostedEditCell;
            _isDraggingSelection = false;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            if (shouldActivateHostedEdit)
            {
                SetCurrentHostedCellEditMode(true);
            }

            _pendingHostedEditActivation = false;
            e.Handled = true;
        }

        if (_isDraggingColumnHeaderSelection && e.ChangedButton == MouseButton.Left)
        {
            _isDraggingColumnHeaderSelection = false;
            _columnHeaderDragAnchorColumn = -1;
            _columnHeaderDragCurrentColumn = -1;
            if (!_isDraggingSelection
                && !_isDraggingRowHeaderSelection
                && !_isResizingColumn
                && !_isResizingRow
                && !_isTrackingColumnMove
                && !_isTrackingRowMove
                && IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            e.Handled = true;
        }

        if (_isDraggingRowHeaderSelection && e.ChangedButton == MouseButton.Left)
        {
            _isDraggingRowHeaderSelection = false;
            _rowHeaderDragAnchorRow = -1;
            _rowHeaderDragCurrentRow = -1;
            if (!_isDraggingSelection
                && !_isResizingColumn
                && !_isResizingRow
                && !_isTrackingColumnMove
                && IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            e.Handled = true;
        }

        if (e.ChangedButton == MouseButton.Right
            && ReferenceEquals(e.OriginalSource, this)
            && _currentCell is { IsValid: true, RowIndex: >= 0 }
            && _currentCell.RowIndex < Rows.Count
            && _currentCell.ColumnIndex >= 0
            && _currentCell.ColumnIndex < Columns.Count
            && Columns[_currentCell.ColumnIndex] is IGriddoHostedColumnView hostedUp
            && TryGetHostedElement(_currentCell) is { } hostUp)
        {
            hostedUp.RelayDirectEditMouseUp(hostUp, e);
            e.Handled = true;
        }

        base.OnMouseUp(e);
    }

    // -------------------------------------------------------------------------
    // Mouse wheel
    // -------------------------------------------------------------------------

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        var ctrlDown = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var inCellEditMode = _isEditing || IsCurrentHostedCellInEditMode();

        // Ctrl+wheel scales the whole grid only when no cell editor is active (hosted Plotto uses Ctrl in-chart).
        if (ctrlDown && !inCellEditMode)
        {
            ContentScale = StepContentScaleStop(ContentScale, e.Delta > 0);
            ShowScaleFeedback();
            e.Handled = true;
            base.OnPreviewMouseWheel(e);
            return;
        }

        // Cell edit mode: wheel (with or without Ctrl) belongs to the editor, not Griddo scale/scroll.
        if (_isEditing)
        {
            e.Handled = true;
        }
        else if (IsCurrentHostedCellInEditMode())
        {
            TryRouteHostedMouseWheelForCell(_currentCell, e);
            e.Handled = true;
        }
        else if (TryRouteHostedMouseWheelZoom(e))
        {
            e.Handled = true;
        }

        base.OnPreviewMouseWheel(e);
    }

    private bool TryRouteHostedMouseWheelForCell(GriddoCellAddress cell, MouseWheelEventArgs e)
    {
        if (!cell.IsValid || cell.ColumnIndex < 0 || cell.ColumnIndex >= Columns.Count)
        {
            return false;
        }

        if (Columns[cell.ColumnIndex] is not IGriddoHostedColumnView hosted)
        {
            return false;
        }

        if (TryGetHostedElement(cell) is not { } host)
        {
            return false;
        }

        return hosted.TryHandleHostedMouseWheel(host, e);
    }

    private bool TryRouteHostedMouseWheelZoom(MouseWheelEventArgs e)
    {
        var pos = e.GetPosition(this);
        var cell = HitTestCell(pos);
        if (!cell.IsValid || Columns[cell.ColumnIndex] is not IGriddoHostedColumnView hosted)
        {
            return false;
        }

        if (TryGetHostedElement(cell) is not { } host)
        {
            return false;
        }

        return hosted.TryHandleHostedMouseWheel(host, e);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (e.Handled)
        {
            base.OnMouseWheel(e);
            return;
        }

        if (IsBodyTransposed)
        {
            if (_verticalScrollBar.Maximum <= 1e-6)
            {
                base.OnMouseWheel(e);
                return;
            }

            var step = e.Delta > 0 ? -48 : 48;
            SetVerticalOffset(_verticalOffset + step);
            e.Handled = true;
            base.OnMouseWheel(e);
            return;
        }

        if (_verticalScrollBar.Maximum <= 0)
        {
            base.OnMouseWheel(e);
            return;
        }

        var delta = e.Delta > 0 ? -GetRowHeight(0) : GetRowHeight(0);
        SetVerticalOffset(_verticalOffset + delta);
        e.Handled = true;
        base.OnMouseWheel(e);
    }
}

