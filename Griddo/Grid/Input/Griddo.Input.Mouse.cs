using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Griddo.Fields;
using Griddo.Editing;
using Griddo.Primitives;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    /// <summary>Minimum pointer travel before field/record move or resize cues activate (DIP).</summary>
    private const double DragCueMinPixels = 1.0;

    /// <summary>Squared DIP distance; right-up farther than this from right-down does not open the cell context menu.</summary>
    private const double BodyRightClickMenuSlopDipSquared = 25.0;

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
                    if (TryGetCurrentField(out var currentField) && currentField.Editor is GriddoNumberCellEditor)
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

        if (e.ChangedButton == MouseButton.Right && HitTestFieldHeader(pointer) is var rightCol and >= 0)
        {
            _headerFocusKind = HeaderFocusKind.Field;
            _headerFocusFieldIndex = rightCol;
            _recordHeaderRightClickOutline.Clear();
            _recordHeaderOnlySelection.Clear();

            var headerAlreadySelected =
                IsFieldHeaderMarkedSelected(rightCol);

            IReadOnlyList<int> contextFieldIndices;
            if (headerAlreadySelected)
            {
                var preserved = new HashSet<int>();
                if (_selectedCells.Count > 0)
                {
                    foreach (var c in GetSelectedFieldIndices())
                    {
                        preserved.Add(c);
                    }
                }
                else
                {
                    preserved.UnionWith(_fieldHeaderOnlySelection);
                }

                _fieldHeaderOnlySelection.Clear();
                foreach (var c in preserved)
                {
                    _fieldHeaderOnlySelection.Add(c);
                }

                _selectedCells.Clear();
                _fieldHeaderRightClickOutline.Clear();
                foreach (var c in preserved)
                {
                    _fieldHeaderRightClickOutline.Add(c);
                }

                contextFieldIndices = preserved.OrderBy(c => c).ToList();
            }
            else
            {
                ClearHeaderAuxiliarySelectionState();
                _selectedCells.Clear();
                _fieldHeaderOnlySelection.Add(rightCol);
                _fieldHeaderRightClickOutline.Add(rightCol);
                contextFieldIndices = [rightCol];
                _currentCell = new GriddoCellAddress(
                    Records.Count == 0 ? 0 : Math.Clamp(_currentCell.RecordIndex, 0, Math.Max(0, Records.Count - 1)),
                    rightCol);
            }

            _hasKeyboardSelectionAnchor = false;
            _isEditing = false;
            FieldHeaderRightClick?.Invoke(this, new GriddoFieldHeaderMouseEventArgs(rightCol, contextFieldIndices, modifiers));
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
            CornerHeaderRightClick?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            CompleteMouseDown(e, handled: true);
            return;
        }

        var rightRecordHeaderHit = HitTestRecordHeader(pointer);
        if (e.ChangedButton == MouseButton.Right && rightRecordHeaderHit >= 0)
        {
            _headerFocusKind = HeaderFocusKind.Record;
            _headerFocusRecordIndex = rightRecordHeaderHit;
            _fieldHeaderRightClickOutline.Clear();
            _fieldHeaderOnlySelection.Clear();

            var recordHeaderAlreadySelected = IsRecordHeaderMarkedSelected(rightRecordHeaderHit);

            IReadOnlyList<int> contextRecordIndices;
            if (recordHeaderAlreadySelected)
            {
                var preserved = new HashSet<int>();
                if (_selectedCells.Count > 0)
                {
                    foreach (var r in GetSelectedRecordIndices())
                    {
                        preserved.Add(r);
                    }
                }
                else
                {
                    preserved.UnionWith(_recordHeaderOnlySelection);
                }

                _recordHeaderOnlySelection.Clear();
                foreach (var r in preserved)
                {
                    _recordHeaderOnlySelection.Add(r);
                }

                _selectedCells.Clear();
                _recordHeaderRightClickOutline.Clear();
                foreach (var r in preserved)
                {
                    _recordHeaderRightClickOutline.Add(r);
                }

                contextRecordIndices = preserved.OrderBy(r => r).ToList();
            }
            else
            {
                ClearHeaderAuxiliarySelectionState();
                _selectedCells.Clear();
                _recordHeaderOnlySelection.Add(rightRecordHeaderHit);
                _recordHeaderRightClickOutline.Add(rightRecordHeaderHit);
                contextRecordIndices = [rightRecordHeaderHit];
                _currentCell = new GriddoCellAddress(
                    rightRecordHeaderHit,
                    Fields.Count == 0 ? 0 : Math.Clamp(_currentCell.FieldIndex, 0, Math.Max(0, Fields.Count - 1)));
            }

            _hasKeyboardSelectionAnchor = false;
            _isEditing = false;
            RecordHeaderRightClick?.Invoke(this, new GriddoRecordHeaderMouseEventArgs(rightRecordHeaderHit, contextRecordIndices));
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

        var clickedFieldHeader = HitTestFieldHeader(pointer);
        if (clickedFieldHeader >= 0)
        {
            if (e is { ChangedButton: MouseButton.Left, ClickCount: 2 })
            {
                ToggleHeaderSort(clickedFieldHeader, additive: isCtrlPressed);
                CompleteMouseDown(e, handled: true);
                return;
            }

            ClearHeaderFocus();
            var target = new GriddoCellAddress(
                Records.Count == 0 ? 0 : Math.Clamp(oldCurrentCell.RecordIndex, 0, Records.Count - 1),
                clickedFieldHeader);
            var clickedSelectedFieldHeader = IsFieldHeaderMarkedSelected(clickedFieldHeader);

            if (e.ChangedButton == MouseButton.Left
                && clickedSelectedFieldHeader
                && !isShiftPressed
                && Records.Count > 0
                && Fields.Count > 0)
            {
                _currentCell = target;
                _isEditing = false;
                InvalidateVisual();
                _isTrackingFieldMove = true;
                _isMovingField = false;
                _fieldMoveStartedFromSelectedHeader = true;
                _movingFieldIndex = clickedFieldHeader;
                _fieldMoveCueIndex = -1;
                _fieldMoveStartPoint = pointer;
                _pendingFieldHeaderSelectionOnMouseUp = true;
                _pendingFieldHeaderIndex = clickedFieldHeader;
                _pendingFieldHeaderSelectionAdditive = isCtrlPressed;
                _pendingFieldHeaderPreserveSelection = clickedSelectedFieldHeader && !isCtrlPressed;
                CaptureMouse();
                CompleteMouseDown(e, handled: true);
                return;
            }

            if (isShiftPressed && oldCurrentCell.IsValid && Records.Count > 0 && Fields.Count > 0)
            {
                SelectRange(oldCurrentCell, target, isCtrlPressed);
                IncludeFieldsRangeForSelectedRecordsOnField(oldCurrentCell.FieldIndex, clickedFieldHeader);
            }
            else
            {
                SelectField(clickedFieldHeader, isCtrlPressed);
            }

            _currentCell = target;
            _isEditing = false;
            InvalidateVisual();
            if (!isShiftPressed
                && e.ChangedButton == MouseButton.Left
                && Records.Count > 0
                && Fields.Count > 0)
            {
                _fieldHeaderDragIsAdditive = isCtrlPressed;
                _selectionDragSnapshot.Clear();
                _selectionDragSnapshot.UnionWith(_selectedCells);
                _fieldHeaderDragAnchorField = clickedFieldHeader;
                _fieldHeaderDragCurrentField = clickedFieldHeader;
                _isDraggingFieldHeaderSelection = true;
                CaptureMouse();
            }

            CompleteMouseDown(e, handled: true);
            return;
        }

        var clickedRecordHeader = HitTestRecordHeader(pointer);
        if (clickedRecordHeader >= 0)
        {
            ClearHeaderFocus();
            var target = new GriddoCellAddress(
                clickedRecordHeader,
                Fields.Count == 0 ? 0 : Math.Clamp(oldCurrentCell.FieldIndex, 0, Fields.Count - 1));
            var clickedSelectedRecordHeader = IsRecordHeaderMarkedSelected(clickedRecordHeader);

            if (e.ChangedButton == MouseButton.Left
                && clickedSelectedRecordHeader
                && !isShiftPressed
                && Records.Count > 0
                && Fields.Count > 0)
            {
                _currentCell = target;
                _isEditing = false;
                InvalidateVisual();
                _isTrackingRecordMove = true;
                _isMovingRecord = false;
                _movingRecordIndex = clickedRecordHeader;
                _recordMoveCueIndex = clickedRecordHeader;
                _recordMoveStartPoint = pointer;
                _pendingRecordHeaderSelectionOnMouseUp = true;
                _pendingRecordHeaderIndex = clickedRecordHeader;
                _pendingRecordHeaderSelectionAdditive = isCtrlPressed;
                _pendingRecordHeaderPreserveSelection = clickedSelectedRecordHeader && !isCtrlPressed;
                CaptureMouse();
                CompleteMouseDown(e, handled: true);
                return;
            }

            if (isShiftPressed && oldCurrentCell.IsValid && Records.Count > 0 && Fields.Count > 0)
            {
                SelectRange(oldCurrentCell, target, isCtrlPressed);
                IncludeRecordsRangeForSelectedFieldsOnRecord(oldCurrentCell.RecordIndex, clickedRecordHeader);
            }
            else
            {
                SelectRecord(clickedRecordHeader, isCtrlPressed);
            }

            if (!isShiftPressed
                && e.ChangedButton == MouseButton.Left
                && Records.Count > 0
                && Fields.Count > 0)
            {
                _recordHeaderDragIsAdditive = isCtrlPressed;
                _selectionDragSnapshot.Clear();
                _selectionDragSnapshot.UnionWith(_selectedCells);
                _recordHeaderDragAnchorRecord = clickedRecordHeader;
                _recordHeaderDragCurrentRecord = clickedRecordHeader;
                _isDraggingRecordHeaderSelection = true;
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

        if (e.ChangedButton == MouseButton.Right)
        {
            ApplyPendingBodyRightContextMenuFromBodyClick(clicked, pointer);
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
                else if (Fields[clicked.FieldIndex] is not IGriddoHostedFieldView)
                {
                    BeginEditWithoutReplacing();
                }

                InvalidateVisual();
                CompleteMouseDown(e, handled: true);
                return;
            }

            if (e.ClickCount == 1
                && Fields[clicked.FieldIndex] is IGriddoHostedFieldView hostedDirect)
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
                && Fields[clicked.FieldIndex] is not IGriddoHostedFieldView
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
                if (Fields[clicked.FieldIndex] is IGriddoHostedFieldView hostedSameCell)
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
            && Records.Count > 0
            && Fields.Count > 0)
        {
            SelectRange(oldCurrentCell, clicked, isCtrlPressed);
            _currentCell = clicked;
            _isEditing = false;
            InvalidateVisual();
            CompleteMouseDown(e, handled: true);
            return;
        }

        if (e.ChangedButton == MouseButton.Left
            && Fields[clicked.FieldIndex] is IGriddoHostedFieldView hostedForEdit
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

    private void ApplyPendingBodyRightContextMenuFromBodyClick(GriddoCellAddress clicked, Point pointer)
    {
        var wasSelected = _selectedCells.Contains(clicked);
        if (!wasSelected)
        {
            _selectedCells.Clear();
            _selectedCells.Add(clicked);
            _currentCell = clicked;
            if (Fields[clicked.FieldIndex] is IGriddoHostedFieldView)
            {
                SyncHostedCells();
            }
        }

        _isEditing = false;
        _pendingBodyRightContextMenuCell = clicked;
        _pendingBodyRightContextMenuDownPos = pointer;
        _pendingBodyRightContextMenuWasAlreadySelected = wasSelected;
        CaptureMouse();
        InvalidateVisual();
    }

    private void OpenCellContextMenuAt(GriddoCellAddress cell, bool cellWasAlreadySelected, Point positionOnGrid)
    {
        var args = new GriddoCellContextMenuEventArgs(cell, positionOnGrid, cellWasAlreadySelected);
        CellContextMenuOpening?.Invoke(this, args);
        if (args.Handled || CellContextMenu is null)
        {
            return;
        }

        CellContextMenu.PlacementTarget = this;
        CellContextMenu.Placement = PlacementMode.RelativePoint;
        CellContextMenu.HorizontalOffset = positionOnGrid.X;
        CellContextMenu.VerticalOffset = positionOnGrid.Y;
        CellContextMenu.IsOpen = true;
    }

    private void ResetPendingBodyRightContextMenu()
    {
        _pendingBodyRightContextMenuCell = new GriddoCellAddress(-1, -1);
    }

    /// <summary>
    /// Hosted Plotto: after right-down + move beyond slop, hand off to the chart so rectangle zoom still works;
    /// grid <see cref="CellContextMenu"/> is skipped for that gesture.
    /// </summary>
    private bool TryPromotePendingBodyRightClickToHostedChart(MouseEventArgs e, Point pointer)
    {
        if (!_pendingBodyRightContextMenuCell.IsValid
            || e.RightButton != MouseButtonState.Pressed)
        {
            return false;
        }

        if (Fields[_pendingBodyRightContextMenuCell.FieldIndex] is not IGriddoHostedFieldView hosted)
        {
            return false;
        }

        var dx = pointer.X - _pendingBodyRightContextMenuDownPos.X;
        var dy = pointer.Y - _pendingBodyRightContextMenuDownPos.Y;
        if (dx * dx + dy * dy <= BodyRightClickMenuSlopDipSquared)
        {
            return false;
        }

        var cell = _pendingBodyRightContextMenuCell;
        ResetPendingBodyRightContextMenu();
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        if (TryGetHostedElement(cell) is not { } host)
        {
            InvalidateVisual();
            return false;
        }

        UpdateLayout();
        host.UpdateLayout();
        var synth = new MouseButtonEventArgs(e.MouseDevice, e.Timestamp, MouseButton.Right);
        _hostedDirectRelayDepth++;
        try
        {
            hosted.RelayDirectEditMouseDown(host, synth);
        }
        finally
        {
            _hostedDirectRelayDepth--;
        }

        InvalidateVisual();
        e.Handled = true;
        return true;
    }

    private bool TryCompletePendingBodyRightContextMenu(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Right || !_pendingBodyRightContextMenuCell.IsValid)
        {
            return false;
        }

        var cell = _pendingBodyRightContextMenuCell;
        var downPos = _pendingBodyRightContextMenuDownPos;
        var wasSel = _pendingBodyRightContextMenuWasAlreadySelected;
        ResetPendingBodyRightContextMenu();

        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        var upPos = e.GetPosition(this);
        var dx = upPos.X - downPos.X;
        var dy = upPos.Y - downPos.Y;
        if (dx * dx + dy * dy <= BodyRightClickMenuSlopDipSquared)
        {
            OpenCellContextMenuAt(cell, wasSel, upPos);
        }

        e.Handled = true;
        return true;
    }

    private void CompleteMouseDown(MouseButtonEventArgs e, bool handled)
    {
        if (handled)
        {
            e.Handled = true;
        }

        base.OnMouseDown(e);
    }

    /// <summary>Field or record divider: double-click autosize, drag starts resize with capture.</summary>
    private bool TryBeginDividerResizeOrAutoSize(MouseButtonEventArgs e, Point pointer, bool isCtrlPressed)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return false;
        }

        var dividerField = HitTestFieldDivider(pointer);
        if (dividerField >= 0)
        {
            if (e.ClickCount == 2)
            {
                if (isCtrlPressed)
                {
                    AutoSizeAllFields();
                    e.Handled = true;
                    return true;
                }

                AutoSizeField(dividerField);
                e.Handled = true;
                return true;
            }

            _isResizingField = true;
            _resizingFieldIndex = dividerField;
            _resizeStartPoint = pointer;
            _resizeInitialSize = GetFieldWidth(dividerField);
            CaptureMouse();
            e.Handled = true;
            return true;
        }

        var dividerRecord = HitTestRecordDivider(pointer);
        if (dividerRecord < 0)
        {
            return false;
        }

        if (e.ClickCount == 2)
        {
            if (isCtrlPressed)
            {
                AutoSizeAllFields();
                e.Handled = true;
                return true;
            }

            AutoSizeRecord(dividerRecord);
            e.Handled = true;
            return true;
        }

        _isResizingRecord = true;
        _resizingRecordIndex = dividerRecord;
        ExitFillRecordsUsingCurrentDisplayedRecordHeight();
        _resizePreserveOldRecordHeight = GetRecordHeight(dividerRecord);
        _resizePreserveOldVerticalOffset = _verticalOffset;
        _resizePreserveOldHorizontalOffset = _horizontalOffset;
        _resizeStartPoint = pointer;
        _resizeInitialSize = GetRecordHeight(dividerRecord);
        CaptureMouse();
        e.Handled = true;
        return true;
    }

    // -------------------------------------------------------------------------
    // Preview mouse down (hosted fields)
    // -------------------------------------------------------------------------

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right)
        {
            var pointerPreview = e.GetPosition(this);
            var clickedPreview = HitTestCell(pointerPreview);
            if (clickedPreview.IsValid
                && Fields[clickedPreview.FieldIndex] is IGriddoHostedFieldView hostedPv
                && TryGetHostedElement(clickedPreview) is { } hostPv
                && !hostedPv.IsHostInEditMode(hostPv))
            {
                ClearHeaderFocus();
                ClearHeaderAuxiliarySelectionState();
                ApplyPendingBodyRightContextMenuFromBodyClick(clickedPreview, pointerPreview);
                e.Handled = true;
            }

            base.OnPreviewMouseDown(e);
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            base.OnPreviewMouseDown(e);
            return;
        }

        ClearHeaderAuxiliarySelectionState();

        var pointer = e.GetPosition(this);
        var clicked = HitTestCell(pointer);
        if (!clicked.IsValid || Fields[clicked.FieldIndex] is not IGriddoHostedFieldView)
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

        if (isShiftPressed && oldCurrentCell.IsValid && Records.Count > 0 && Fields.Count > 0)
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
        UpdateFieldHeaderTooltip(pointer);
        if (TryPromotePendingBodyRightClickToHostedChart(e, pointer))
        {
            base.OnMouseMove(e);
            return;
        }

        if (_isDraggingEditSelection && e.LeftButton == MouseButtonState.Pressed)
        {
            var caretIndex = GetCaretIndexFromEditPoint(pointer);
            _editSession.SetCaretIndex(caretIndex, extendSelection: true);
            InvalidateVisual();
            e.Handled = true;
            base.OnMouseMove(e);
            return;
        }

        if (_isResizingField)
        {
            var delta = IsBodyTransposed ? pointer.Y - _resizeStartPoint.Y : pointer.X - _resizeStartPoint.X;
            SetFieldWidth(_resizingFieldIndex, _resizeInitialSize + delta);
            InvalidateVisual();
            e.Handled = true;
            base.OnMouseMove(e);
            return;
        }

        if (_isResizingRecord)
        {
            double requestedHeight;
            if (IsBodyTransposed)
            {
                var bodyPx = pointer.X - _recordHeaderWidth;
                requestedHeight = GetUniformRecordHeightScreenFromDividerBodyX(_resizingRecordIndex, bodyPx);
            }
            else
            {
                var bodyPy = pointer.Y - ScaledFieldHeaderHeight;
                requestedHeight = GetUniformRecordHeightScreenFromDividerBodyY(_resizingRecordIndex, bodyPy);
            }

            SetRecordHeightKeepingRecordTop(_resizingRecordIndex, requestedHeight);
            InvalidateVisual();
            e.Handled = true;
            base.OnMouseMove(e);
            return;
        }

        if (_isTrackingFieldMove)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                StopFieldMoveTracking();
                base.OnMouseMove(e);
                return;
            }

            var dragDistance = (pointer - _fieldMoveStartPoint).Length;
            var isPointerInFieldHeader = HitTestFieldHeader(pointer) >= 0;
            var shouldShowMovingHeaderCue = isPointerInFieldHeader && dragDistance >= DragCueMinPixels;
            if (_isMovingPointerInFieldHeader != shouldShowMovingHeaderCue)
            {
                _isMovingPointerInFieldHeader = shouldShowMovingHeaderCue;
                InvalidateVisual();
            }

            if (!_isMovingField)
            {
                if (dragDistance >= DragCueMinPixels)
                {
                    _isMovingField = true;
                }
            }

            if (_isMovingField)
            {
                AutoScrollDuringFieldMove(pointer.X);
                var targetField = HitTestFieldHeaderDrag(pointer);
                if (targetField >= 0 && targetField != _movingFieldIndex)
                {
                    _fieldMoveCueIndex = targetField;
                    InvalidateVisual();
                }
                else if (_fieldMoveCueIndex != -1)
                {
                    _fieldMoveCueIndex = -1;
                    InvalidateVisual();
                }
            }

            e.Handled = true;
            base.OnMouseMove(e);
            return;
        }

        if (_isTrackingRecordMove)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                StopRecordMoveTracking();
                base.OnMouseMove(e);
                return;
            }

            var dragDistance = (pointer - _recordMoveStartPoint).Length;
            if (!_isMovingRecord && dragDistance >= DragCueMinPixels)
            {
                _isMovingRecord = true;
            }

            AutoScrollDuringRecordInteraction(pointer.Y);
            var targetRecord = HitTestRecordHeaderDrag(pointer);
            if (targetRecord >= 0 && targetRecord != _recordMoveCueIndex)
            {
                _recordMoveCueIndex = targetRecord;
                InvalidateVisual();
            }

            e.Handled = true;
            base.OnMouseMove(e);
            return;
        }

        if (_isDraggingFieldHeaderSelection
            && IsMouseCaptured
            && e.LeftButton == MouseButtonState.Pressed)
        {
            AutoScrollDuringFieldMove(pointer.X);
            var hoveredFieldHeader = HitTestFieldHeaderDrag(pointer);
            if (hoveredFieldHeader >= 0 && hoveredFieldHeader != _fieldHeaderDragCurrentField)
            {
                _fieldHeaderDragCurrentField = hoveredFieldHeader;
                ApplyFieldHeaderDragSelection();
                InvalidateVisual();
                e.Handled = true;
            }

            base.OnMouseMove(e);
            return;
        }

        if (_isDraggingRecordHeaderSelection
            && IsMouseCaptured
            && e.LeftButton == MouseButtonState.Pressed)
        {
            AutoScrollDuringRecordInteraction(pointer.Y);
            var hoveredRecordHeader = HitTestRecordHeaderDrag(pointer);
            if (hoveredRecordHeader >= 0 && hoveredRecordHeader != _recordHeaderDragCurrentRecord)
            {
                _recordHeaderDragCurrentRecord = hoveredRecordHeader;
                ApplyRecordHeaderDragSelection();
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
        UpdateFieldHeaderTooltip(Mouse.GetPosition(this));
        if (ReferenceEquals(ToolTip, _fieldHeaderToolTip)
            && IsEmptyFieldHeaderToolTipContent(_fieldHeaderToolTip.Content))
        {
            e.Handled = true;
        }

        base.OnToolTipOpening(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        // Pointer left the grid; allow the next header hover to run a fresh ToolTip attach cycle.
        // Do not reset while the field-header tooltip is open — the pointer often leaves the grid to read the popup.
        if (ReferenceEquals(ToolTip, _fieldHeaderToolTip) && !_fieldHeaderToolTip.IsOpen)
        {
            ClearFieldHeaderToolTipContent();
            _fieldHeaderToolTipNeedsReattach = true;
            _priorPointerOnDescribedFieldHeader = false;
        }

        base.OnMouseLeave(e);
    }

    private void FieldHeaderToolTipOnClosed(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_fieldHeaderToolTipClosedSuppress > 0)
        {
            return;
        }

        _fieldHeaderToolTipNeedsReattach = true;
        _priorPointerOnDescribedFieldHeader = false;
        if (ReferenceEquals(ToolTip, _fieldHeaderToolTip))
        {
            _fieldHeaderToolTip.Content = null;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, RefreshFieldHeaderTooltipIfApplicable);
    }

    private void RefreshFieldHeaderTooltipIfApplicable()
    {
        if (!IsLoaded || !IsVisible || !ReferenceEquals(ToolTip, _fieldHeaderToolTip))
        {
            return;
        }

        UpdateFieldHeaderTooltip(Mouse.GetPosition(this));
    }

    private void UpdateFieldHeaderTooltip(Point pointer)
    {
        if (!ReferenceEquals(ToolTip, _fieldHeaderToolTip))
        {
            return;
        }

        var headerCol = HitTestFieldHeader(pointer);
        var inStrip = headerCol >= 0 && headerCol < Fields.Count;
        if (inStrip && TryGetFieldHeaderDescription(Fields[headerCol], out var text))
        {
            // Reattach when (a) tooltip service asked for it after close, or (b) pointer re-enters a described
            // header from body / undescribed header — WPF will not reopen on the same attach after leaving the strip
            // if we only rely on ToolTip.Closed (it may not run when IsOpen is cleared from mouse moves).
            if (!_priorPointerOnDescribedFieldHeader || _fieldHeaderToolTipNeedsReattach)
            {
                _fieldHeaderToolTipClosedSuppress++;
                try
                {
                    ToolTip = null;
                    ToolTip = _fieldHeaderToolTip;
                }
                finally
                {
                    _fieldHeaderToolTipClosedSuppress--;
                }

                _fieldHeaderToolTipNeedsReattach = false;
            }

            _priorPointerOnDescribedFieldHeader = true;
            ApplyFieldHeaderToolTipText(text);
            return;
        }

        _priorPointerOnDescribedFieldHeader = false;

        if (inStrip)
        {
            if (_fieldHeaderToolTip.IsOpen)
            {
                _fieldHeaderToolTip.IsOpen = false;
            }

            ClearFieldHeaderToolTipContent();
            _fieldHeaderToolTipNeedsReattach = true;
            return;
        }

        if (_fieldHeaderToolTip.IsOpen)
        {
            _fieldHeaderToolTip.IsOpen = false;
        }

        ClearFieldHeaderToolTipContent();
        _fieldHeaderToolTipNeedsReattach = true;
    }

    private void ApplyFieldHeaderToolTipText(string text)
    {
        if (_fieldHeaderToolTip.Content is TextBlock tb)
        {
            tb.Text = text;
            return;
        }

        _fieldHeaderToolTip.Content = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 480,
            Focusable = false,
            Foreground = SystemColors.InfoTextBrush
        };
    }

    private void ClearFieldHeaderToolTipContent()
    {
        _fieldHeaderToolTip.Content = null;
    }

    private static bool IsEmptyFieldHeaderToolTipContent(object? content) =>
        content switch
        {
            null => true,
            string s => string.IsNullOrWhiteSpace(s),
            TextBlock tb => string.IsNullOrWhiteSpace(tb.Text),
            _ => false
        };

    private static bool TryGetFieldHeaderDescription(IGriddoFieldView field, out string text)
    {
        if (field is IGriddoFieldDescriptionView descriptionView
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

    private void AutoScrollDuringFieldMove(double pointerX)
    {
        if (Fields.Count == 0 || _viewportBodyWidth <= 0)
        {
            return;
        }

        var scrollStart = _recordHeaderWidth + GetFixedFieldsWidth();
        var scrollEnd = _recordHeaderWidth + _viewportBodyWidth;
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

    private void AutoScrollDuringRecordInteraction(double pointerY)
    {
        if (Records.Count == 0 || _viewportBodyHeight <= 0)
        {
            return;
        }

        var scrollStartY = ScaledFieldHeaderHeight;
        var scrollEndY = ScaledFieldHeaderHeight + _viewportBodyHeight;
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
        if (Records.Count == 0 || Fields.Count == 0 || _viewportBodyWidth <= 0 || _viewportBodyHeight <= 0)
        {
            return;
        }

        const double edgeBand = 24.0;
        const double maxHorizontalSpeed = 48.0;
        const double maxVerticalSpeed = 36.0;

        var scrollStartX = _recordHeaderWidth + GetFixedFieldsWidth();
        var scrollEndX = _recordHeaderWidth + _viewportBodyWidth;
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

        var scrollStartY = ScaledFieldHeaderHeight;
        var scrollEndY = ScaledFieldHeaderHeight + _viewportBodyHeight;
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
        if (TryCompletePendingBodyRightContextMenu(e))
        {
            base.OnMouseUp(e);
            return;
        }

        if (_isDraggingEditSelection && e.ChangedButton == MouseButton.Left)
        {
            _isDraggingEditSelection = false;
            if (!_isDraggingSelection
                && !_isDraggingFieldHeaderSelection
                && !_isDraggingRecordHeaderSelection
                && !_isResizingField
                && !_isResizingRecord
                && !_isTrackingFieldMove
                && !_isTrackingRecordMove
                && IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
        }

        if (_isTrackingFieldMove && e.ChangedButton == MouseButton.Left)
        {
            if (_isMovingField &&
                _movingFieldIndex >= 0 &&
                _fieldMoveCueIndex >= 0 &&
                _movingFieldIndex != _fieldMoveCueIndex)
            {
                if (_fieldMoveStartedFromSelectedHeader)
                {
                    MoveSelectedFields(_movingFieldIndex, _fieldMoveCueIndex);
                }
                else
                {
                    MoveField(_movingFieldIndex, _fieldMoveCueIndex);
                }
                InvalidateVisual();
            }
            else if (_pendingFieldHeaderSelectionOnMouseUp
                && _pendingFieldHeaderIndex >= 0)
            {
                if (!_pendingFieldHeaderPreserveSelection)
                {
                    SelectField(_pendingFieldHeaderIndex, _pendingFieldHeaderSelectionAdditive);
                }

                _currentCell = new GriddoCellAddress(
                    Records.Count == 0 ? 0 : Math.Clamp(_currentCell.RecordIndex, 0, Records.Count - 1),
                    _pendingFieldHeaderIndex);
                _isEditing = false;
                InvalidateVisual();
            }

            StopFieldMoveTracking();
            InvalidateVisual();
            e.Handled = true;
        }

        if (_isTrackingRecordMove && e.ChangedButton == MouseButton.Left)
        {
            if (_isMovingRecord &&
                _movingRecordIndex >= 0 &&
                _recordMoveCueIndex >= 0 &&
                _movingRecordIndex != _recordMoveCueIndex)
            {
                MoveSelectedRecords(_movingRecordIndex, _recordMoveCueIndex);
                InvalidateVisual();
            }
            else if (_pendingRecordHeaderSelectionOnMouseUp
                && _pendingRecordHeaderIndex >= 0)
            {
                if (!_pendingRecordHeaderPreserveSelection)
                {
                    SelectRecord(_pendingRecordHeaderIndex, _pendingRecordHeaderSelectionAdditive);
                }

                _currentCell = new GriddoCellAddress(
                    _pendingRecordHeaderIndex,
                    Fields.Count == 0 ? 0 : Math.Clamp(_currentCell.FieldIndex, 0, Fields.Count - 1));
                _isEditing = false;
                InvalidateVisual();
            }

            StopRecordMoveTracking();
            e.Handled = true;
        }

        if (_isResizingField && e.ChangedButton == MouseButton.Left)
        {
            _isResizingField = false;
            _resizingFieldIndex = -1;
            FieldWidthsChanged?.Invoke(this, EventArgs.Empty);
            if (!_isDraggingSelection && IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            e.Handled = true;
        }

        if (_isResizingRecord && e.ChangedButton == MouseButton.Left)
        {
            _isResizingRecord = false;
            var savedDivider = _resizingRecordIndex;
            _resizingRecordIndex = -1;
            if (savedDivider >= 0)
            {
                ApplyInteractiveRecordResizeScrollPreservation(
                    savedDivider,
                    _resizePreserveOldRecordHeight,
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

        if (_isDraggingFieldHeaderSelection && e.ChangedButton == MouseButton.Left)
        {
            _isDraggingFieldHeaderSelection = false;
            _fieldHeaderDragAnchorField = -1;
            _fieldHeaderDragCurrentField = -1;
            if (!_isDraggingSelection
                && !_isDraggingRecordHeaderSelection
                && !_isResizingField
                && !_isResizingRecord
                && !_isTrackingFieldMove
                && !_isTrackingRecordMove
                && IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            e.Handled = true;
        }

        if (_isDraggingRecordHeaderSelection && e.ChangedButton == MouseButton.Left)
        {
            _isDraggingRecordHeaderSelection = false;
            _recordHeaderDragAnchorRecord = -1;
            _recordHeaderDragCurrentRecord = -1;
            if (!_isDraggingSelection
                && !_isResizingField
                && !_isResizingRecord
                && !_isTrackingFieldMove
                && IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            e.Handled = true;
        }

        if (e.ChangedButton == MouseButton.Right
            && ReferenceEquals(e.OriginalSource, this)
            && _currentCell is { IsValid: true, RecordIndex: >= 0 }
            && _currentCell.RecordIndex < Records.Count
            && _currentCell.FieldIndex >= 0
            && _currentCell.FieldIndex < Fields.Count
            && Fields[_currentCell.FieldIndex] is IGriddoHostedFieldView hostedUp
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
        if (!cell.IsValid || cell.FieldIndex < 0 || cell.FieldIndex >= Fields.Count)
        {
            return false;
        }

        if (Fields[cell.FieldIndex] is not IGriddoHostedFieldView hosted)
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
        if (!cell.IsValid || Fields[cell.FieldIndex] is not IGriddoHostedFieldView hosted)
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

        var delta = e.Delta > 0 ? -GetRecordHeight(0) : GetRecordHeight(0);
        SetVerticalOffset(_verticalOffset + delta);
        e.Handled = true;
        base.OnMouseWheel(e);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        ResetPendingBodyRightContextMenu();
        base.OnLostMouseCapture(e);
    }
}

