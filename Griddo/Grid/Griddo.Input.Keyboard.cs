using System.Linq;
using System.Windows.Input;

namespace Griddo;

public sealed partial class Griddo
{
    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        if (!TryGetCurrentColumn(out var column))
        {
            return;
        }

        if (!_isEditing)
        {
            if (column is IGriddoHostedColumnView)
            {
                return;
            }

            var ch = e.Text.FirstOrDefault();
            if (ch != default && column.Editor.CanStartWith(ch))
            {
                _editSession.Start(column.Editor.BeginEdit(GetCurrentValue(), ch));
                _isEditing = true;
                InvalidateVisual();
            }

            return;
        }

        _editSession.InsertText(e.Text);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        var isCtrlPressed = (modifiers & ModifierKeys.Control) != 0;
        var isShiftPressed = (modifiers & ModifierKeys.Shift) != 0;
        var isHostedEditing = IsCurrentHostedCellInEditMode();

        if (isCtrlPressed && e.Key == Key.F)
        {
            if (TryPromptFindText(out var findText))
            {
                _findText = findText;
                AddFindHistory(findText);
                RebuildFindMatches();
                FindNextMatch(forward: true, fromCurrentMatch: false);
                InvalidateVisual();
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.F3)
        {
            if (string.IsNullOrWhiteSpace(_findText))
            {
                if (!TryPromptFindText(out var prompted))
                {
                    e.Handled = true;
                    return;
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
            return;
        }

        if (e.Key == Key.Escape && !string.IsNullOrEmpty(_findText))
        {
            _findText = string.Empty;
            _findMatchCell = new GriddoCellAddress(-1, -1);
            _findMatchedCells.Clear();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if ((isCtrlPressed && e.Key == Key.C) || (isCtrlPressed && e.Key == Key.Insert))
        {
            if (_isEditing)
            {
                CopyEditBufferToClipboard();
            }
            else
            {
                CopySelectedCellsToClipboard();
            }

            e.Handled = true;
            return;
        }

        if ((isCtrlPressed && e.Key == Key.V) || (isShiftPressed && e.Key == Key.Insert))
        {
            if (_isEditing)
            {
                PasteClipboardIntoEditBuffer();
            }
            else
            {
                PasteClipboardIntoGrid();
            }

            e.Handled = true;
            return;
        }

        if ((isCtrlPressed && e.Key == Key.X) || (isShiftPressed && e.Key == Key.Delete))
        {
            if (_isEditing)
            {
                CutEditBufferToClipboard();
            }
            else
            {
                CutSelectedCellsToClipboard();
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && _isEditing)
        {
            CommitEdit();
            MoveCurrentCell(isShiftPressed ? -1 : 1, 0);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && isHostedEditing)
        {
            SetCurrentHostedCellEditMode(false);
            MoveCurrentCell(isShiftPressed ? -1 : 1, 0);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _isEditing)
        {
            _isEditing = false;
            _editSession.Clear();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && isHostedEditing)
        {
            SetCurrentHostedCellEditMode(false);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Tab && _isEditing)
        {
            CommitEdit();
            MoveCurrentCell(0, isShiftPressed ? -1 : 1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Tab && isHostedEditing)
        {
            SetCurrentHostedCellEditMode(false);
            MoveCurrentCell(0, isShiftPressed ? -1 : 1);
            e.Handled = true;
            return;
        }

        if (_isEditing)
        {
            switch (e.Key)
            {
                case Key.Left:
                    _editSession.MoveCaretLeft(isCtrlPressed, isShiftPressed);
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                case Key.Right:
                    _editSession.MoveCaretRight(isCtrlPressed, isShiftPressed);
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                case Key.Home:
                    _editSession.MoveCaretHome(isShiftPressed);
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                case Key.End:
                    _editSession.MoveCaretEnd(isShiftPressed);
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                case Key.Back:
                    if (_editSession.Backspace())
                    {
                        InvalidateVisual();
                    }
                    e.Handled = true;
                    return;
                case Key.Delete:
                    if (_editSession.DeleteForward())
                    {
                        InvalidateVisual();
                    }

                    e.Handled = true;
                    return;
                case Key.Up:
                case Key.Down:
                    e.Handled = true;
                    return;
            }
        }

        if (!_isEditing)
        {
            if (e.Key == Key.Enter)
            {
                MoveCurrentCell(isShiftPressed ? -1 : 1, 0);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Tab)
            {
                MoveCurrentCell(0, isShiftPressed ? -1 : 1);
                e.Handled = true;
                return;
            }

            if (HandleCellKeyboardNavigation(e.Key, isCtrlPressed, isShiftPressed))
            {
                e.Handled = true;
                return;
            }

            switch (e.Key)
            {
                case Key.F2:
                    BeginCurrentCellEdit();
                    e.Handled = true;
                    break;
                case Key.Delete:
                    ClearSelectedCells();
                    e.Handled = true;
                    break;
            }
        }

        base.OnKeyDown(e);
    }
}

