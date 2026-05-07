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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        var isCtrlPressed = (modifiers & ModifierKeys.Control) != 0;
        var isShiftPressed = (modifiers & ModifierKeys.Shift) != 0;
        var isHostedEditing = IsCurrentHostedCellInEditMode();

        if (TryHandleFindOpenChord(e, isCtrlPressed)
            || TryHandleFindNextF3(e, isShiftPressed)
            || TryHandleClearFindOnEscape(e)
            || TryHandleClipboardCopy(e, isCtrlPressed)
            || TryHandleClipboardPaste(e, isCtrlPressed, isShiftPressed)
            || TryHandleClipboardCut(e, isCtrlPressed, isShiftPressed)
            || TryHandleFillDown(e, isCtrlPressed, isHostedEditing)
            || TryHandleIncrementalDown(e, isCtrlPressed, isHostedEditing)
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
            if (e.NewFocus is DependencyObject newFocus
                && TryGetHostedElement(_currentCell) is FrameworkElement host
                && (ReferenceEquals(newFocus, host) || IsVisualDescendantOf(newFocus, host)))
            {
                base.OnLostKeyboardFocus(e);
                return;
            }

            SetCurrentHostedCellEditMode(false);
        }

        base.OnLostKeyboardFocus(e);
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

    private bool TryHandleFindNextF3(KeyEventArgs e, bool isShiftPressed)
    {
        if (e.Key != Key.F3)
        {
            return false;
        }

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
            FindNextMatch(forward: !isShiftPressed, fromCurrentMatch: false);
        }
        else
        {
            RebuildFindMatches();
            FindNextMatch(forward: !isShiftPressed, fromCurrentMatch: true);
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
            _currentCell = new GriddoCellAddress(0, 0);
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

        if (HandleCellKeyboardNavigation(e.Key, isCtrlPressed, isShiftPressed))
        {
            e.Handled = true;
            return true;
        }

        return false;
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
