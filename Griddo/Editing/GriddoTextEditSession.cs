namespace Griddo.Editing;

internal sealed class GriddoTextEditSession
{
    public string Buffer { get; private set; } = string.Empty;
    public int CaretIndex { get; private set; }
    public int SelectionAnchor { get; private set; } = -1;

    public void Start(string buffer)
    {
        Buffer = buffer ?? string.Empty;
        CaretIndex = Buffer.Length;
        SelectionAnchor = -1;
    }

    public void Clear()
    {
        Buffer = string.Empty;
        CaretIndex = 0;
        SelectionAnchor = -1;
    }

    public void MoveCaretLeft(bool ctrlPressed, bool shiftPressed)
    {
        var target = ctrlPressed ? FindPreviousWordBoundary(CaretIndex) : Math.Max(0, CaretIndex - 1);
        MoveCaret(target, shiftPressed);
    }

    public void MoveCaretRight(bool ctrlPressed, bool shiftPressed)
    {
        var target = ctrlPressed ? FindNextWordBoundary(CaretIndex) : Math.Min(Buffer.Length, CaretIndex + 1);
        MoveCaret(target, shiftPressed);
    }

    public void MoveCaretHome(bool shiftPressed) => MoveCaret(0, shiftPressed);

    public void MoveCaretEnd(bool shiftPressed) => MoveCaret(Buffer.Length, shiftPressed);

    public void SetCaretIndex(int targetIndex, bool extendSelection)
        => MoveCaret(targetIndex, extendSelection);

    public void SelectWordAt(int index)
    {
        if (Buffer.Length == 0)
        {
            CaretIndex = 0;
            SelectionAnchor = -1;
            return;
        }

        var clamped = Math.Clamp(index, 0, Buffer.Length);
        if (clamped == Buffer.Length)
        {
            clamped = Math.Max(0, Buffer.Length - 1);
        }

        var start = clamped;
        var end = clamped;

        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
        var inWord = clamped < Buffer.Length && IsWordChar(Buffer[clamped]);

        if (inWord)
        {
            while (start > 0 && IsWordChar(Buffer[start - 1]))
            {
                start--;
            }

            while (end < Buffer.Length && IsWordChar(Buffer[end]))
            {
                end++;
            }
        }
        else
        {
            while (start > 0 && !IsWordChar(Buffer[start - 1]) && !char.IsWhiteSpace(Buffer[start - 1]))
            {
                start--;
            }

            while (end < Buffer.Length && !IsWordChar(Buffer[end]) && !char.IsWhiteSpace(Buffer[end]))
            {
                end++;
            }

            if (start == end)
            {
                CaretIndex = clamped;
                SelectionAnchor = -1;
                return;
            }
        }

        SelectionAnchor = start;
        CaretIndex = end;
    }

    public void SelectAll()
    {
        if (Buffer.Length == 0)
        {
            CaretIndex = 0;
            SelectionAnchor = -1;
            return;
        }

        SelectionAnchor = 0;
        CaretIndex = Buffer.Length;
    }

    public void ReplaceBuffer(string buffer)
    {
        Buffer = buffer ?? string.Empty;
        CaretIndex = Buffer.Length;
        SelectionAnchor = -1;
    }

    public void InsertText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        DeleteSelection();
        CaretIndex = Math.Clamp(CaretIndex, 0, Buffer.Length);
        Buffer = Buffer.Insert(CaretIndex, text);
        CaretIndex += text.Length;
        SelectionAnchor = -1;
    }

    public bool Backspace()
    {
        if (DeleteSelection())
        {
            return true;
        }

        if (CaretIndex <= 0 || Buffer.Length == 0)
        {
            return false;
        }

        Buffer = Buffer.Remove(CaretIndex - 1, 1);
        CaretIndex--;
        SelectionAnchor = -1;
        return true;
    }

    public bool DeleteForward()
    {
        if (DeleteSelection())
        {
            return true;
        }

        if (CaretIndex >= Buffer.Length)
        {
            return false;
        }

        Buffer = Buffer.Remove(CaretIndex, 1);
        SelectionAnchor = -1;
        return true;
    }

    public string GetCopyText()
    {
        if (TryGetSelection(out var start, out var end))
        {
            return Buffer[start..end];
        }

        return Buffer;
    }

    public string CutText()
    {
        var copied = GetCopyText();
        if (TryGetSelection(out _, out _))
        {
            DeleteSelection();
        }
        else
        {
            Clear();
        }

        return copied;
    }

    public bool TryGetSelection(out int start, out int end)
    {
        start = 0;
        end = 0;
        if (SelectionAnchor < 0)
        {
            return false;
        }

        var anchor = Math.Clamp(SelectionAnchor, 0, Buffer.Length);
        var caret = Math.Clamp(CaretIndex, 0, Buffer.Length);
        if (anchor == caret)
        {
            return false;
        }

        start = Math.Min(anchor, caret);
        end = Math.Max(anchor, caret);
        return true;
    }

    public string GetSanitizedClipboardText()
    {
        if (!System.Windows.Clipboard.ContainsText())
        {
            return string.Empty;
        }

        return System.Windows.Clipboard.GetText().Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
    }

    private void MoveCaret(int targetIndex, bool extendSelection)
    {
        var clamped = Math.Clamp(targetIndex, 0, Buffer.Length);
        if (extendSelection)
        {
            if (SelectionAnchor < 0)
            {
                SelectionAnchor = CaretIndex;
            }

            CaretIndex = clamped;
            if (SelectionAnchor == CaretIndex)
            {
                SelectionAnchor = -1;
            }

            return;
        }

        CaretIndex = clamped;
        SelectionAnchor = -1;
    }

    private bool DeleteSelection()
    {
        if (!TryGetSelection(out var start, out var end))
        {
            return false;
        }

        Buffer = Buffer.Remove(start, end - start);
        CaretIndex = start;
        SelectionAnchor = -1;
        return true;
    }

    private int FindPreviousWordBoundary(int caretIndex)
    {
        var index = Math.Clamp(caretIndex, 0, Buffer.Length);
        if (index == 0)
        {
            return 0;
        }

        while (index > 0 && !char.IsLetterOrDigit(Buffer[index - 1]))
        {
            index--;
        }

        while (index > 0 && char.IsLetterOrDigit(Buffer[index - 1]))
        {
            index--;
        }

        return index;
    }

    private int FindNextWordBoundary(int caretIndex)
    {
        var index = Math.Clamp(caretIndex, 0, Buffer.Length);
        if (index >= Buffer.Length)
        {
            return Buffer.Length;
        }

        while (index < Buffer.Length && char.IsLetterOrDigit(Buffer[index]))
        {
            index++;
        }

        while (index < Buffer.Length && !char.IsLetterOrDigit(Buffer[index]))
        {
            index++;
        }

        return index;
    }
}
