using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Griddo.Fields;
using Griddo.Primitives;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    // -------------------------------------------------------------------------
    // Text input
    // -------------------------------------------------------------------------

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        if (!TryGetCurrentField(out var field))
        {
            return;
        }

        if (!_isEditing)
        {
            if (field is IGriddoHostedFieldView)
            {
                return;
            }

            var ch = e.Text.FirstOrDefault();
            if (ch != default && field.Editor.CanStartWith(ch))
            {
                _editSession.Start(field.Editor.BeginEdit(GetCurrentValue(), ch));
                _isEditing = true;
                InvalidateVisual();
            }

            return;
        }

        _editSession.InsertText(e.Text);
        InvalidateVisual();
    }

    // -------------------------------------------------------------------------
    // Keyboard
    // -------------------------------------------------------------------------

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        var isCtrlPressed = (modifiers & ModifierKeys.Control) != 0;

        // Capture Ctrl+PageUp/PageDown before parent containers (eg tab controls)
        // can consume these chords for tab switching.
        if (isCtrlPressed && (e.Key == Key.PageUp || e.Key == Key.PageDown))
        {
            var isShiftPressed = (modifiers & ModifierKeys.Shift) != 0;
            if (TryHandlePageNavigation(e, isCtrlPressed: true, isShiftPressed))
            {
                return;
            }
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        var isCtrlPressed = (modifiers & ModifierKeys.Control) != 0;
        var isShiftPressed = (modifiers & ModifierKeys.Shift) != 0;
        var isHostedEditing = IsCurrentHostedCellInEditMode();

        if (TryHandleFindOpenChord(e, isCtrlPressed)
            || TryHandleFindNextF3(e, isShiftPressed, isCtrlPressed)
            || TryHandleClearFindOnEscape(e)
            || TryHandleClipboardCopyWithHeaders(e, isCtrlPressed, isShiftPressed)
            || TryHandleClipboardCopy(e, isCtrlPressed)
            || TryHandleClipboardPaste(e, isCtrlPressed, isShiftPressed)
            || TryHandleClipboardCut(e, isCtrlPressed, isShiftPressed)
            || TryHandleFillDown(e, isCtrlPressed, isHostedEditing)
            || TryHandleIncrementalDown(e, isCtrlPressed, isHostedEditing)
            || TryHandleExportToExcel(e, isCtrlPressed, isShiftPressed)
            || TryHandleEnterWhileEditingOrHosted(e, isShiftPressed, isHostedEditing)
            || TryHandleEscapeWhileEditingOrHosted(e, isHostedEditing)
            || TryHandleTabWhileEditingOrHosted(e, isShiftPressed, isHostedEditing)
            || TryHandleTextEditCaretKeys(e, isCtrlPressed, isShiftPressed)
            || TryHandleCtrlASelectAllCells(e, isCtrlPressed, isHostedEditing)
            || TryHandleSpaceToggleBool(e)
            || TryHandleGridNavigationWithoutEdit(e, isCtrlPressed, isShiftPressed)
            || TryHandleF2OrDeleteOutsideEdit(e))
        {
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        if (_activeEditOptionsMenu is { IsOpen: true })
        {
            base.OnLostKeyboardFocus(e);
            return;
        }

        if (_isEditing)
        {
            CommitEdit();
        }
        else if (IsCurrentHostedCellInEditMode())
        {
            if (e.NewFocus is DependencyObject newFocus)
            {
                if (TryGetHostedElement(_currentCell) is FrameworkElement host
                    && (ReferenceEquals(newFocus, host) || IsVisualDescendantOf(newFocus, host)))
                {
                    base.OnLostKeyboardFocus(e);
                    return;
                }

                if (IsFocusInsideBodyCellContextMenu(newFocus))
                {
                    base.OnLostKeyboardFocus(e);
                    return;
                }
            }
            else if (ShouldKeepHostedEditDuringBodyContextMenuGesture())
            {
                base.OnLostKeyboardFocus(e);
                return;
            }

            SetCurrentHostedCellEditMode(false);
        }

        base.OnLostKeyboardFocus(e);
        if (HideSelectionWhenGridLosesFocus)
        {
            InvalidateVisual();
        }
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (HideSelectionWhenGridLosesFocus)
        {
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Body <see cref="CellContextMenu"/> (and deferred open) steal keyboard focus from the hosted chart;
    /// do not exit hosted edit mode or the red outline and menu flash closed.
    /// </summary>
    private bool ShouldKeepHostedEditDuringBodyContextMenuGesture()
    {
        if (CellContextMenu is { IsOpen: true })
        {
            return true;
        }

        return _deferredBodyCellContextMenuTimer is not null;
    }

    private bool IsFocusInsideBodyCellContextMenu(DependencyObject focus)
    {
        if (CellContextMenu is not { IsOpen: true } menu)
        {
            return false;
        }

        if (ReferenceEquals(focus, menu) || IsVisualDescendantOf(focus, menu))
        {
            return true;
        }

        for (var d = focus; d is not null; d = LogicalTreeHelper.GetParent(d))
        {
            if (ReferenceEquals(d, menu))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsVisualDescendantOf(DependencyObject? node, DependencyObject ancestor)
    {
        if (node is null)
        {
            return false;
        }

        for (var d = node; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (ReferenceEquals(d, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    // -------------------------------------------------------------------------
    // Find
    // -------------------------------------------------------------------------

    private bool TryHandleFindOpenChord(KeyEventArgs e, bool isCtrlPressed)
    {
        if (!isCtrlPressed || e.Key != Key.F)
        {
            return false;
        }

        if (TryPromptFindText(out var findText))
        {
            _findText = findText;
            AddFindHistory(findText);
            RebuildFindMatches();
            FindNextMatch(forward: true, fromCurrentMatch: false);
            InvalidateVisual();
        }

        e.Handled = true;
        return true;
    }

    private bool TryHandleFindNextF3(KeyEventArgs e, bool isShiftPressed, bool isCtrlPressed)
    {
        if (e.Key != Key.F3)
        {
            return false;
        }

        var findForward = !(isShiftPressed || isCtrlPressed);

        if (string.IsNullOrWhiteSpace(_findText))
        {
            if (!TryPromptFindText(out var prompted))
            {
                e.Handled = true;
                return true;
            }

            _findText = prompted;
            AddFindHistory(prompted);
            RebuildFindMatches();
            FindNextMatch(forward: findForward, fromCurrentMatch: false);
        }
        else
        {
            RebuildFindMatches();
            FindNextMatch(forward: findForward, fromCurrentMatch: true);
        }

        InvalidateVisual();
        e.Handled = true;
        return true;
    }

    private bool TryHandleClearFindOnEscape(KeyEventArgs e)
    {
        if (e.Key != Key.Escape || string.IsNullOrEmpty(_findText))
        {
            return false;
        }

        _findText = string.Empty;
        _findMatchCell = new GriddoCellAddress(-1, -1);
        _findMatchedCells.Clear();
        InvalidateVisual();
        e.Handled = true;
        return true;
    }

    // -------------------------------------------------------------------------
    // Clipboard
    // -------------------------------------------------------------------------

    private bool TryHandleClipboardCopy(KeyEventArgs e, bool isCtrlPressed)
    {
        if (!((isCtrlPressed && e.Key == Key.C) || (isCtrlPressed && e.Key == Key.Insert)))
        {
            return false;
        }

        if (_isEditing)
        {
            CopyEditBufferToClipboard();
        }
        else
        {
            CopySelectedCellsToClipboard();
        }

        e.Handled = true;
        return true;
    }

    private bool TryHandleClipboardCopyWithHeaders(KeyEventArgs e, bool isCtrlPressed, bool isShiftPressed)
    {
        if (!isCtrlPressed || !isShiftPressed || e.Key != Key.C)
        {
            return false;
        }

        if (_isEditing)
        {
            CopyEditBufferToClipboard();
        }
        else
        {
            CopyToClipboardWithHeaders();
        }

        e.Handled = true;
        return true;
    }

    private bool TryHandleClipboardPaste(KeyEventArgs e, bool isCtrlPressed, bool isShiftPressed)
    {
        if (!((isCtrlPressed && e.Key == Key.V) || (isShiftPressed && e.Key == Key.Insert)))
        {
            return false;
        }

        if (_isEditing)
        {
            PasteClipboardIntoEditBuffer();
        }
        else
        {
            PasteClipboardIntoGrid();
        }

        e.Handled = true;
        return true;
    }

    private bool TryHandleClipboardCut(KeyEventArgs e, bool isCtrlPressed, bool isShiftPressed)
    {
        if (!((isCtrlPressed && e.Key == Key.X) || (isShiftPressed && e.Key == Key.Delete)))
        {
            return false;
        }

        if (_isEditing)
        {
            CutEditBufferToClipboard();
        }
        else
        {
            CutSelectedCellsToClipboard();
        }

        e.Handled = true;
        return true;
    }

    private bool TryHandleExportToExcel(KeyEventArgs e, bool isCtrlPressed, bool isShiftPressed)
    {
        if (!isCtrlPressed || isShiftPressed || e.Key != Key.E || _isEditing)
        {
            return false;
        }

        ExportSelectionToExcel();
        e.Handled = true;
        return true;
    }

    // -------------------------------------------------------------------------
    // Enter / Escape / Tab — inline or hosted edit
    // -------------------------------------------------------------------------

    private bool TryHandleEnterWhileEditingOrHosted(KeyEventArgs e, bool isShiftPressed, bool isHostedEditing)
    {
        if (e.Key != Key.Enter)
        {
            return false;
        }

        if (_isEditing)
        {
            CommitEdit();
            MoveCurrentCell(isShiftPressed ? -1 : 1, 0);
            e.Handled = true;
            return true;
        }

        if (isHostedEditing)
        {
            SetCurrentHostedCellEditMode(false);
            MoveCurrentCell(isShiftPressed ? -1 : 1, 0);
            e.Handled = true;
            return true;
        }

        return false;
    }

    private bool TryHandleEscapeWhileEditingOrHosted(KeyEventArgs e, bool isHostedEditing)
    {
        if (e.Key != Key.Escape)
        {
            return false;
        }

        if (_isEditing)
        {
            _isEditing = false;
            _editSession.Clear();
            InvalidateVisual();
            e.Handled = true;
            return true;
        }

        if (isHostedEditing)
        {
            SetCurrentHostedCellEditMode(false);
            e.Handled = true;
            return true;
        }

        return false;
    }

    private bool TryHandleTabWhileEditingOrHosted(KeyEventArgs e, bool isShiftPressed, bool isHostedEditing)
    {
        if (e.Key != Key.Tab)
        {
            return false;
        }

        if (_isEditing)
        {
            CommitEdit();
            MoveCurrentCell(0, isShiftPressed ? -1 : 1);
            e.Handled = true;
            return true;
        }

        if (isHostedEditing)
        {
            SetCurrentHostedCellEditMode(false);
            MoveCurrentCell(0, isShiftPressed ? -1 : 1);
            e.Handled = true;
            return true;
        }

        return false;
    }

    // -------------------------------------------------------------------------
    // Text edit session — caret / delete
    // -------------------------------------------------------------------------

    private bool TryHandleTextEditCaretKeys(KeyEventArgs e, bool isCtrlPressed, bool isShiftPressed)
    {
        if (!_isEditing)
        {
            return false;
        }

        if (isCtrlPressed && e.Key == Key.A)
        {
            _editSession.SelectAll();
            InvalidateVisual();
            e.Handled = true;
            return true;
        }

        switch (e.Key)
        {
            case Key.Left:
                _editSession.MoveCaretLeft(isCtrlPressed, isShiftPressed);
                InvalidateVisual();
                e.Handled = true;
                return true;
            case Key.Right:
                _editSession.MoveCaretRight(isCtrlPressed, isShiftPressed);
                InvalidateVisual();
                e.Handled = true;
                return true;
            case Key.Home:
                _editSession.MoveCaretHome(isShiftPressed);
                InvalidateVisual();
                e.Handled = true;
                return true;
            case Key.End:
                _editSession.MoveCaretEnd(isShiftPressed);
                InvalidateVisual();
                e.Handled = true;
                return true;
            case Key.Back:
                if (_editSession.Backspace())
                {
                    InvalidateVisual();
                }

                e.Handled = true;
                return true;
            case Key.Delete:
                if (_editSession.DeleteForward())
                {
                    InvalidateVisual();
                }

                e.Handled = true;
                return true;
            case Key.Up:
            case Key.Down:
                e.Handled = true;
                return true;
            default:
                return false;
        }
    }

    // -------------------------------------------------------------------------
    // Grid navigation — not in text edit
    // -------------------------------------------------------------------------

    private bool TryHandleCtrlASelectAllCells(KeyEventArgs e, bool isCtrlPressed, bool isHostedEditing)
    {
        if (!isCtrlPressed || e.Key != Key.A || _isEditing || isHostedEditing)
        {
            return false;
        }

        SelectAllCells();
        _hasKeyboardSelectionAnchor = false;
        _isEditing = false;
        if (Records.Count > 0 && Fields.Count > 0)
        {
            AssignCurrentCell(new GriddoCellAddress(0, 0));
        }

        InvalidateVisual();
        e.Handled = true;
        return true;
    }

    private bool TryHandleSpaceToggleBool(KeyEventArgs e)
    {
        if (e.Key != Key.Space || _isEditing)
        {
            return false;
        }

        if (!_currentCell.IsValid || _currentCell.RecordIndex < 0 || _currentCell.RecordIndex >= Records.Count
            || _currentCell.FieldIndex < 0 || _currentCell.FieldIndex >= Fields.Count)
        {
            return false;
        }

        if (!IsCheckboxToggleCell(_currentCell))
        {
            return false;
        }

        ToggleBoolCell(_currentCell);
        e.Handled = true;
        return true;
    }

    private bool TryHandleGridNavigationWithoutEdit(KeyEventArgs e, bool isCtrlPressed, bool isShiftPressed)
    {
        if (_isEditing)
        {
            return false;
        }

        if (e.Key == Key.Enter)
        {
            MoveCurrentCell(isShiftPressed ? -1 : 1, 0);
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Tab)
        {
            MoveCurrentCell(0, isShiftPressed ? -1 : 1);
            e.Handled = true;
            return true;
        }

        if (TryHandlePageNavigation(e, isCtrlPressed, isShiftPressed))
        {
            return true;
        }

        if (HandleCellKeyboardNavigation(e.Key, isCtrlPressed, isShiftPressed))
        {
            e.Handled = true;
            return true;
        }

        return false;
    }

    private bool TryHandlePageNavigation(KeyEventArgs e, bool isCtrlPressed, bool isShiftPressed)
    {
        if (e.Key != Key.PageUp && e.Key != Key.PageDown)
        {
            return false;
        }

        if (Records.Count == 0 || Fields.Count == 0)
        {
            e.Handled = true;
            return true;
        }

        var direction = e.Key == Key.PageUp ? -1 : 1;
        var target = isCtrlPressed
            ? GetHorizontalPageNavigationTarget(direction)
            : GetVerticalPageNavigationTarget(direction);

        ApplyKeyboardNavigationTarget(target, isShiftPressed);
        e.Handled = true;
        return true;
    }

    private GriddoCellAddress GetVerticalPageNavigationTarget(int direction)
    {
        var recordHeight = Math.Max(1, GetRecordHeight(0));
        var viewportRecords = (int)Math.Floor(Math.Max(1, GetScrollRecordsViewportHeight()) / recordHeight);
        var recordStep = Math.Max(1, viewportRecords);
        var targetRecord = Math.Clamp(_currentCell.RecordIndex + direction * recordStep, 0, Records.Count - 1);

        var pageDelta = Math.Max(1, _verticalScrollBar.LargeChange);
        SetVerticalOffset(_verticalOffset + direction * pageDelta);

        return new GriddoCellAddress(targetRecord, _currentCell.FieldIndex);
    }

    private GriddoCellAddress GetHorizontalPageNavigationTarget(int direction)
    {
        var targetField = GetHorizontalPageTargetField(direction);
        var pageDelta = Math.Max(1, _horizontalScrollBar.LargeChange);
        SetHorizontalOffset(_horizontalOffset + direction * pageDelta);
        return new GriddoCellAddress(_currentCell.RecordIndex, targetField);
    }

    private int GetHorizontalPageTargetField(int direction)
    {
        if (direction < 0)
        {
            var widthBudget = Math.Max(1, GetScrollViewportWidth());
            var consumed = 0.0;
            var field = _currentCell.FieldIndex;
            while (field > 0 && consumed < widthBudget)
            {
                field--;
                consumed += GetFieldWidth(field);
            }

            return Math.Clamp(field, 0, Fields.Count - 1);
        }

        var forwardBudget = Math.Max(1, GetScrollViewportWidth());
        var forwardConsumed = 0.0;
        var targetField = _currentCell.FieldIndex;
        while (targetField < Fields.Count - 1 && forwardConsumed < forwardBudget)
        {
            targetField++;
            forwardConsumed += GetFieldWidth(targetField);
        }

        return Math.Clamp(targetField, 0, Fields.Count - 1);
    }

    private bool TryHandleF2OrDeleteOutsideEdit(KeyEventArgs e)
    {
        if (_isEditing)
        {
            return false;
        }

        switch (e.Key)
        {
            case Key.F2:
                BeginCurrentCellEdit();
                e.Handled = true;
                return true;
            case Key.Delete:
                ClearSelectedCells();
                e.Handled = true;
                return true;
            default:
                return false;
        }
    }
}
