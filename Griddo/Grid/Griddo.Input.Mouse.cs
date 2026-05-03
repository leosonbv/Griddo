using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Griddo;

public sealed partial class Griddo
{
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        Keyboard.Focus(this);
        _pendingHostedEditActivation = false;
        _hasKeyboardSelectionAnchor = false;
        var pointer = e.GetPosition(this);
        var modifiers = Keyboard.Modifiers;
        var isCtrlPressed = (modifiers & ModifierKeys.Control) != 0;
        var isShiftPressed = (modifiers & ModifierKeys.Shift) != 0;
        var oldCurrentCell = _currentCell;

        if (e.ChangedButton == MouseButton.Right && HitTestColumnHeader(pointer) is var rightHeader && rightHeader >= 0)
        {
            if (!IsColumnSelected(rightHeader))
            {
                SelectColumn(rightHeader, additive: false);
                _currentCell = new GriddoCellAddress(
                    Rows.Count == 0 ? 0 : Math.Clamp(_currentCell.RowIndex, 0, Rows.Count - 1),
                    rightHeader);
                _isEditing = false;
                InvalidateVisual();
            }

            ColumnHeaderRightClick?.Invoke(this, new GriddoColumnHeaderMouseEventArgs(rightHeader));
            e.Handled = true;
            base.OnMouseDown(e);
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            var dividerColumn = HitTestColumnDivider(pointer);
            if (dividerColumn >= 0)
            {
                if (e.ClickCount == 2)
                {
                    if (isCtrlPressed)
                    {
                        AutoSizeAllColumns();
                        e.Handled = true;
                        return;
                    }

                    AutoSizeColumn(dividerColumn);
                    e.Handled = true;
                    return;
                }

                _isResizingColumn = true;
                _resizingColumnIndex = dividerColumn;
                _resizeStartPoint = pointer;
                _resizeInitialSize = GetColumnWidth(dividerColumn);
                CaptureMouse();
                e.Handled = true;
                return;
            }

            var dividerRow = HitTestRowDivider(pointer);
            if (dividerRow >= 0)
            {
                if (e.ClickCount == 2)
                {
                    if (isCtrlPressed)
                    {
                        AutoSizeAllColumns();
                        e.Handled = true;
                        return;
                    }

                    AutoSizeRow(dividerRow);
                    e.Handled = true;
                    return;
                }

                _isResizingRow = true;
                _resizingRowIndex = dividerRow;
                _resizeStartPoint = pointer;
                _resizeInitialSize = GetRowHeight(dividerRow);
                CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        if (HitTestTopLeftHeaderCell(pointer))
        {
            SelectAllCells();
            _isEditing = false;
            InvalidateVisual();
            e.Handled = true;
            base.OnMouseDown(e);
            return;
        }

        var clickedColumnHeader = HitTestColumnHeader(pointer);
        if (clickedColumnHeader >= 0)
        {
            var target = new GriddoCellAddress(
                Rows.Count == 0 ? 0 : Math.Clamp(oldCurrentCell.RowIndex, 0, Rows.Count - 1),
                clickedColumnHeader);
            var clickedSelectedColumnHeader = IsColumnSelected(clickedColumnHeader);

            if (e.ChangedButton == MouseButton.Left
                && clickedSelectedColumnHeader
                && !isShiftPressed
                && Rows.Count > 0
                && Columns.Count > 0)
            {
                _isTrackingColumnMove = true;
                _isMovingColumn = false;
                _columnMoveStartedFromSelectedHeader = true;
                _movingColumnIndex = clickedColumnHeader;
                _columnMoveCueIndex = -1;
                _columnMoveStartPoint = pointer;
                _pendingColumnHeaderSelectionOnMouseUp = true;
                _pendingColumnHeaderIndex = clickedColumnHeader;
                _pendingColumnHeaderSelectionAdditive = isCtrlPressed;
                CaptureMouse();
                e.Handled = true;
                base.OnMouseDown(e);
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

            e.Handled = true;
            base.OnMouseDown(e);
            return;
        }

        var clickedRowHeader = HitTestRowHeader(pointer);
        if (clickedRowHeader >= 0)
        {
            var target = new GriddoCellAddress(
                clickedRowHeader,
                Columns.Count == 0 ? 0 : Math.Clamp(oldCurrentCell.ColumnIndex, 0, Columns.Count - 1));
            var clickedSelectedRowHeader = IsRowSelected(clickedRowHeader);

            if (e.ChangedButton == MouseButton.Left
                && clickedSelectedRowHeader
                && !isShiftPressed
                && Rows.Count > 0
                && Columns.Count > 0)
            {
                _isTrackingRowMove = true;
                _isMovingRow = false;
                _movingRowIndex = clickedRowHeader;
                _rowMoveCueIndex = clickedRowHeader;
                _rowMoveStartPoint = pointer;
                _pendingRowHeaderSelectionOnMouseUp = true;
                _pendingRowHeaderIndex = clickedRowHeader;
                _pendingRowHeaderSelectionAdditive = isCtrlPressed;
                CaptureMouse();
                e.Handled = true;
                base.OnMouseDown(e);
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
            e.Handled = true;
            base.OnMouseDown(e);
            return;
        }

        var clicked = HitTestCell(pointer);
        if (!clicked.IsValid)
        {
            base.OnMouseDown(e);
            return;
        }

        if (e.ChangedButton == MouseButton.Right
            && HostedPlotDirectEditOnMouseDown
            && e.ClickCount == 1
            && Columns[clicked.ColumnIndex] is IGriddoHostedColumnView hostedRightDirect)
        {
            _selectedCells.Clear();
            _selectedCells.Add(clicked);
            _currentCell = clicked;
            _isDraggingSelection = false;
            _pendingHostedEditActivation = false;
            SyncHostedCells();
            SetCurrentHostedCellEditMode(true);
            if (TryGetHostedElement(clicked) is FrameworkElement hostForRightRelay)
            {
                hostedRightDirect.RelayDirectEditMouseDown(hostForRightRelay, e);
            }

            _isEditing = false;
            InvalidateVisual();
            e.Handled = true;
            base.OnMouseDown(e);
            return;
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            var wasSelected = _selectedCells.Contains(clicked);
            if (!wasSelected)
            {
                _selectedCells.Clear();
                _selectedCells.Add(clicked);
                _currentCell = clicked;
            }

            _isEditing = false;
            InvalidateVisual();
            OpenCellContextMenu(e, clicked, wasSelected);
            e.Handled = true;
            base.OnMouseDown(e);
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
                if (Columns[clicked.ColumnIndex] is not IGriddoHostedColumnView)
                {
                    BeginEditWithoutReplacing();
                }

                InvalidateVisual();
                e.Handled = true;
                base.OnMouseDown(e);
                return;
            }

            if (HostedPlotDirectEditOnMouseDown
                && e.ClickCount == 1
                && Columns[clicked.ColumnIndex] is IGriddoHostedColumnView hostedDirect)
            {
                if (TryGetHostedElement(clicked) is FrameworkElement hostedForDirect
                    && hostedDirect.IsHostInEditMode(hostedForDirect))
                {
                    InvalidateVisual();
                    base.OnMouseDown(e);
                    return;
                }

                _selectedCells.Clear();
                _selectedCells.Add(clicked);
                _currentCell = clicked;
                _isDraggingSelection = false;
                _pendingHostedEditActivation = false;
                SyncHostedCells();
                SetCurrentHostedCellEditMode(true);
                if (TryGetHostedElement(clicked) is FrameworkElement hostForRelay)
                {
                    hostedDirect.RelayDirectEditMouseDown(hostForRelay, e);
                }

                InvalidateVisual();
                e.Handled = true;
                base.OnMouseDown(e);
                return;
            }

            if (e.ClickCount == 1
                && oldCurrentCell.IsValid
                && clicked == oldCurrentCell)
            {
                if (Columns[clicked.ColumnIndex] is IGriddoHostedColumnView hostedSameCell)
                {
                    if (TryGetHostedElement(clicked) is FrameworkElement hostedSameElement
                        && hostedSameCell.IsHostInEditMode(hostedSameElement))
                    {
                        InvalidateVisual();
                        base.OnMouseDown(e);
                        return;
                    }

                    _pendingHostedEditActivation = true;
                    _pendingHostedEditCell = clicked;
                }
                else
                {
                    _selectedCells.Clear();
                    _selectedCells.Add(clicked);
                    _currentCell = clicked;
                    _isDraggingSelection = false;
                    BeginEditWithoutReplacing();
                    InvalidateVisual();
                    e.Handled = true;
                    base.OnMouseDown(e);
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
            e.Handled = true;
            base.OnMouseDown(e);
            return;
        }

        if (e.ChangedButton == MouseButton.Left
            && Columns[clicked.ColumnIndex] is IGriddoHostedColumnView hostedForEdit
            && TryGetHostedElement(clicked) is FrameworkElement hostedElement
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
            base.OnMouseDown(e);
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            base.OnMouseDown(e);
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
        e.Handled = true;
        base.OnMouseDown(e);
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

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
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

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pointer = e.GetPosition(this);
        if (_isResizingColumn)
        {
            var delta = pointer.X - _resizeStartPoint.X;
            SetColumnWidth(_resizingColumnIndex, _resizeInitialSize + delta);
            InvalidateVisual();
            e.Handled = true;
            base.OnMouseMove(e);
            return;
        }

        if (_isResizingRow)
        {
            var delta = pointer.Y - _resizeStartPoint.Y;
            SetRowHeightKeepingRowTop(_resizingRowIndex, _resizeInitialSize + delta);
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
            var shouldShowMovingHeaderCue = isPointerInColumnHeader && dragDistance >= 1;
            if (_isMovingPointerInColumnHeader != shouldShowMovingHeaderCue)
            {
                _isMovingPointerInColumnHeader = shouldShowMovingHeaderCue;
                InvalidateVisual();
            }

            if (!_isMovingColumn)
            {
                if (dragDistance >= 1)
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
            if (!_isMovingRow && dragDistance >= 1)
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

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
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
                SelectColumn(_pendingColumnHeaderIndex, _pendingColumnHeaderSelectionAdditive);
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
                SelectRow(_pendingRowHeaderIndex, _pendingRowHeaderSelectionAdditive);
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
            _resizingRowIndex = -1;
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
            if (!_isDraggingSelection && !_isResizingColumn && !_isResizingRow && !_isTrackingColumnMove && IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            e.Handled = true;
        }

        if (e.ChangedButton == MouseButton.Right
            && ReferenceEquals(e.OriginalSource, this)
            && _currentCell.IsValid
            && _currentCell.RowIndex >= 0
            && _currentCell.RowIndex < Rows.Count
            && _currentCell.ColumnIndex >= 0
            && _currentCell.ColumnIndex < Columns.Count
            && Columns[_currentCell.ColumnIndex] is IGriddoHostedColumnView hostedUp
            && TryGetHostedElement(_currentCell) is FrameworkElement hostUp)
        {
            hostedUp.RelayDirectEditMouseUp(hostUp, e);
            e.Handled = true;
        }

        base.OnMouseUp(e);
    }

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

        if (TryGetHostedElement(cell) is not FrameworkElement host)
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

        if (TryGetHostedElement(cell) is not FrameworkElement host)
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

