using System.Windows;
using System.Windows.Controls;

namespace Griddo;

public sealed partial class Griddo
{
    private bool IsCurrentHostedCellInEditMode()
    {
        if (_currentCell.RowIndex < 0 || _currentCell.RowIndex >= Rows.Count || _currentCell.ColumnIndex < 0 || _currentCell.ColumnIndex >= Columns.Count)
        {
            return false;
        }

        if (Columns[_currentCell.ColumnIndex] is not IGriddoHostedColumnView hostedColumn)
        {
            return false;
        }

        return TryGetHostedElement(_currentCell) is FrameworkElement host && hostedColumn.IsHostInEditMode(host);
    }

    private bool IsHostedCellInEditMode(GriddoCellAddress cell)
    {
        if (cell.RowIndex < 0 || cell.RowIndex >= Rows.Count || cell.ColumnIndex < 0 || cell.ColumnIndex >= Columns.Count)
        {
            return false;
        }

        if (Columns[cell.ColumnIndex] is not IGriddoHostedColumnView hostedColumn)
        {
            return false;
        }

        return TryGetHostedElement(cell) is FrameworkElement host && hostedColumn.IsHostInEditMode(host);
    }

    private void SetCurrentHostedCellEditMode(bool isEditing)
    {
        if (_currentCell.RowIndex < 0 || _currentCell.RowIndex >= Rows.Count || _currentCell.ColumnIndex < 0 || _currentCell.ColumnIndex >= Columns.Count)
        {
            return;
        }

        if (Columns[_currentCell.ColumnIndex] is not IGriddoHostedColumnView hostedColumn)
        {
            return;
        }

        if (TryGetHostedElement(_currentCell) is not FrameworkElement host)
        {
            return;
        }

        hostedColumn.SetHostEditMode(host, isEditing);
        InvalidateVisual();
    }

    private void BeginCurrentCellEdit()
    {
        if (_currentCell.RowIndex < 0 || _currentCell.RowIndex >= Rows.Count || _currentCell.ColumnIndex < 0 || _currentCell.ColumnIndex >= Columns.Count)
        {
            return;
        }

        if (Columns[_currentCell.ColumnIndex] is IGriddoHostedColumnView)
        {
            SetCurrentHostedCellEditMode(true);
            return;
        }

        BeginEditWithoutReplacing();
    }


    private bool TryGetCurrentColumn(out IGriddoColumnView column)
    {
        if (_currentCell.ColumnIndex < 0 || _currentCell.ColumnIndex >= Columns.Count)
        {
            column = default!;
            return false;
        }

        column = Columns[_currentCell.ColumnIndex];
        return true;
    }

    private object? GetCurrentValue()
    {
        if (_currentCell.RowIndex < 0 || _currentCell.RowIndex >= Rows.Count || !TryGetCurrentColumn(out var column))
        {
            return null;
        }

        return column.GetValue(Rows[_currentCell.RowIndex]);
    }

    private void BeginEditWithoutReplacing()
    {
        if (!TryGetCurrentColumn(out var column))
        {
            return;
        }

        if (column is IGriddoHostedColumnView)
        {
            return;
        }

        _editSession.Start(column.Editor.BeginEdit(GetCurrentValue()));
        _isEditing = true;
        InvalidateVisual();
    }

    private void CommitEdit()
    {
        if (!_isEditing || !TryGetCurrentColumn(out var column))
        {
            return;
        }

        if (_currentCell.RowIndex < 0 || _currentCell.RowIndex >= Rows.Count)
        {
            return;
        }

        if (column.Editor.TryCommit(_editSession.Buffer, out var newValue))
        {
            column.TrySetValue(Rows[_currentCell.RowIndex], newValue);
        }

        _isEditing = false;
        _editSession.Clear();
        InvalidateVisual();
    }

    private void InsertIntoEditBuffer(string text)
    {
        _editSession.InsertText(text);
    }

    private void PasteClipboardIntoEditBuffer()
    {
        var text = _editSession.GetSanitizedClipboardText();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        _editSession.InsertText(text);
        InvalidateVisual();
    }

    private void CopyEditBufferToClipboard()
    {
        Clipboard.SetText(_editSession.GetCopyText());
    }

    private void CutEditBufferToClipboard()
    {
        Clipboard.SetText(_editSession.CutText());
        InvalidateVisual();
    }
}
