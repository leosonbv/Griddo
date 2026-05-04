using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Griddo.Columns;
using Griddo.Editing;
using Griddo.Primitives;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    /// <summary>Starts edit mode for the current cell (same behavior as F2).</summary>
    public void EditCurrentCell() => BeginCurrentCellEdit();

    /// <summary>Cancels current cell edit mode (same behavior as Esc while editing).</summary>
    public void CancelCurrentCellEdit()
    {
        CloseActiveEditOptionsMenu();
        if (_isEditing)
        {
            _isEditing = false;
            _editSession.Clear();
            InvalidateVisual();
            return;
        }

        if (IsCurrentHostedCellInEditMode())
        {
            SetCurrentHostedCellEditMode(false);
        }
    }

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

        return TryGetHostedElement(_currentCell) is { } host && hostedColumn.IsHostInEditMode(host);
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

        return TryGetHostedElement(cell) is { } host && hostedColumn.IsHostInEditMode(host);
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

        if (TryGetHostedElement(_currentCell) is not { } host)
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
        CloseActiveEditOptionsMenu();
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
        if (column.Editor is IGriddoOptionsCellEditor optionsEditor)
        {
            OpenEditOptionsMenu(optionsEditor);
        }

        InvalidateVisual();
    }

    private void CommitEdit()
    {
        if (_isCommittingEdit)
        {
            return;
        }

        CloseActiveEditOptionsMenu();
        if (!_isEditing || !TryGetCurrentColumn(out var column))
        {
            return;
        }

        if (_currentCell.RowIndex < 0 || _currentCell.RowIndex >= Rows.Count)
        {
            return;
        }

        _isCommittingEdit = true;
        try
        {
            if (column.Editor.TryCommit(_editSession.Buffer, out var newValue))
            {
                column.TrySetValue(Rows[_currentCell.RowIndex], newValue);
            }
        }
        finally
        {
            _isCommittingEdit = false;
        }

        _isEditing = false;
        _editSession.Clear();
        InvalidateVisual();
    }

    private void OpenEditOptionsMenu(IGriddoOptionsCellEditor optionsEditor)
    {
        if (!_currentCell.IsValid)
        {
            return;
        }

        var cellRect = GetCellRect(_currentCell.RowIndex, _currentCell.ColumnIndex);
        if (cellRect.IsEmpty)
        {
            return;
        }

        CloseActiveEditOptionsMenu();

        var selectedValues = optionsEditor.ParseValues(_editSession.Buffer)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rowSource = _currentCell.RowIndex >= 0 && _currentCell.RowIndex < Rows.Count
            ? Rows[_currentCell.RowIndex]
            : null;
        var options = optionsEditor is IGriddoContextualOptionsCellEditor contextualEditor
            ? contextualEditor.GetOptions(rowSource)
            : optionsEditor.Options;
        var menu = new ContextMenu
        {
            PlacementTarget = this,
            Placement = PlacementMode.RelativePoint,
            HorizontalOffset = cellRect.X + 2,
            VerticalOffset = Math.Min(cellRect.Bottom, ActualHeight - 2)
        };

        foreach (var option in options)
        {
            var localOption = option;
            var item = new MenuItem
            {
                Header = BuildOptionsMenuHeader(optionsEditor, rowSource, localOption),
                IsCheckable = true,
                IsChecked = selectedValues.Contains(localOption),
                StaysOpenOnClick = optionsEditor.AllowMultiple
            };
            item.Click += (_, _) =>
            {
                if (optionsEditor.AllowMultiple)
                {
                    if (item.IsChecked)
                    {
                        selectedValues.Add(localOption);
                    }
                    else
                    {
                        selectedValues.Remove(localOption);
                    }

                    _editSession.ReplaceBuffer(optionsEditor.FormatValues(selectedValues));
                    InvalidateVisual();
                }
                else
                {
                    _editSession.ReplaceBuffer(localOption);
                    CommitEdit();
                    menu.IsOpen = false;
                }
            };
            menu.Items.Add(item);
        }

        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeEditOptionsMenu, menu))
            {
                _activeEditOptionsMenu = null;
                if (_isEditing && TryGetCurrentColumn(out var currentCol) && ReferenceEquals(currentCol.Editor, optionsEditor))
                {
                    CommitEdit();
                }
            }
        };

        _activeEditOptionsMenu = menu;
        menu.IsOpen = true;
    }

    private void CloseActiveEditOptionsMenu()
    {
        if (_activeEditOptionsMenu is { IsOpen: true } menu)
        {
            menu.IsOpen = false;
        }

        _activeEditOptionsMenu = null;
    }

    private static object BuildOptionsMenuHeader(IGriddoOptionsCellEditor optionsEditor, object? rowSource, string option)
    {
        if (optionsEditor is not IGriddoSwatchOptionsCellEditor swatchEditor
            || !swatchEditor.TryGetSwatchBrush(option, out var swatchBrush))
        {
            if (optionsEditor is IGriddoContextualOptionsCellEditor contextualEditor
                && contextualEditor.TryGetOptionExample(rowSource, option, out var example))
            {
                var grid = new System.Windows.Controls.Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var nameText = new TextBlock
                {
                    Text = option,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var exampleText = new TextBlock
                {
                    Text = example,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    TextAlignment = TextAlignment.Right,
                    Foreground = Brushes.DimGray,
                    VerticalAlignment = VerticalAlignment.Center
                };
                System.Windows.Controls.Grid.SetColumn(nameText, 0);
                System.Windows.Controls.Grid.SetColumn(exampleText, 2);
                grid.Children.Add(nameText);
                grid.Children.Add(exampleText);
                return grid;
            }

            return option;
        }

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        panel.Children.Add(new Border
        {
            Width = 24,
            Height = 12,
            Margin = new Thickness(0, 0, 6, 0),
            BorderBrush = Brushes.DimGray,
            BorderThickness = new Thickness(1),
            Background = swatchBrush
        });
        panel.Children.Add(new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Text = option
        });
        return panel;
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
        System.Windows.Clipboard.SetText(_editSession.GetCopyText());
    }

    private void CutEditBufferToClipboard()
    {
        System.Windows.Clipboard.SetText(_editSession.CutText());
        InvalidateVisual();
    }

    private bool TryGetEditTextLayout(out FormattedText editText, out Rect editContentRect, out Point textOrigin)
    {
        editText = null!;
        editContentRect = Rect.Empty;
        textOrigin = default;

        if (!_isEditing || !_currentCell.IsValid || !TryGetCurrentColumn(out var column))
        {
            return false;
        }

        var rect = GetCellRect(_currentCell.RowIndex, _currentCell.ColumnIndex);
        if (rect.IsEmpty)
        {
            return false;
        }

        const double editContentInset = 1.0;
        var fullEditRect = new Rect(
            rect.X + editContentInset,
            rect.Y + editContentInset,
            Math.Max(0, rect.Width - (editContentInset * 2)),
            Math.Max(0, rect.Height - (editContentInset * 2)));
        if (fullEditRect.IsEmpty)
        {
            return false;
        }

        editContentRect = GetInlineEditTextRect(fullEditRect);
        if (editContentRect.IsEmpty)
        {
            return false;
        }

        var typeface = new Typeface("Segoe UI");
        var fontSize = EffectiveFontSize;
        editText = new FormattedText(
            _editSession.Buffer,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            1.0);
        editText.TextAlignment = column.ContentAlignment;
        editText.MaxTextWidth = Math.Max(1, editContentRect.Width - 8);
        editText.MaxTextHeight = Math.Max(1, editContentRect.Height - 4);
        editText.Trimming = TextTrimming.CharacterEllipsis;

        var contentWidth = Math.Max(1, editContentRect.Width - 8);
        var totalTextWidth = Math.Min(editText.WidthIncludingTrailingWhitespace, contentWidth);
        var textStartX = editContentRect.X + 4;
        if (column.ContentAlignment == TextAlignment.Right)
        {
            textStartX += Math.Max(0, contentWidth - totalTextWidth);
        }
        else if (column.ContentAlignment == TextAlignment.Center)
        {
            textStartX += Math.Max(0, (contentWidth - totalTextWidth) / 2);
        }

        var textY = editContentRect.Y + Math.Max(0, (editContentRect.Height - editText.Height) / 2);
        textOrigin = new Point(textStartX, textY);
        return true;
    }

    private static readonly double DialogEditButtonWidth = 20;
    private static readonly double DialogEditButtonGap = 3;

    private bool IsCurrentEditorDialogButton()
        => _isEditing
            && TryGetCurrentColumn(out var column)
            && column.Editor is IGriddoDialogButtonCellEditor;

    private Rect GetInlineEditTextRect(Rect fullEditRect)
    {
        if (!IsCurrentEditorDialogButton())
        {
            return fullEditRect;
        }

        var reservedWidth = DialogEditButtonWidth + DialogEditButtonGap + 2;
        return new Rect(
            fullEditRect.X,
            fullEditRect.Y,
            Math.Max(1, fullEditRect.Width - reservedWidth),
            fullEditRect.Height);
    }

    private bool TryGetInlineDialogButtonRect(Rect fullEditRect, out Rect buttonRect)
    {
        buttonRect = Rect.Empty;
        if (!IsCurrentEditorDialogButton() || fullEditRect.IsEmpty)
        {
            return false;
        }

        var width = Math.Min(DialogEditButtonWidth, Math.Max(0, fullEditRect.Width - 2));
        var x = fullEditRect.Right - width - 1;
        buttonRect = new Rect(
            x,
            fullEditRect.Y + 1,
            width,
            Math.Max(0, fullEditRect.Height - 2));
        return !buttonRect.IsEmpty;
    }

    private bool TryGetCurrentInlineDialogButtonRect(out Rect buttonRect)
    {
        buttonRect = Rect.Empty;
        if (!_isEditing || !_currentCell.IsValid)
        {
            return false;
        }

        var rect = GetCellRect(_currentCell.RowIndex, _currentCell.ColumnIndex);
        if (rect.IsEmpty)
        {
            return false;
        }

        const double editContentInset = 1.0;
        var fullEditRect = new Rect(
            rect.X + editContentInset,
            rect.Y + editContentInset,
            Math.Max(0, rect.Width - (editContentInset * 2)),
            Math.Max(0, rect.Height - (editContentInset * 2)));
        return TryGetInlineDialogButtonRect(fullEditRect, out buttonRect);
    }

    private bool TriggerInlineDialogButton()
    {
        if (!TryGetCurrentColumn(out var column)
            || column.Editor is not IGriddoDialogButtonCellEditor dialogEditor)
        {
            return false;
        }

        _editSession.ReplaceBuffer(dialogEditor.LaunchToken);
        CommitEdit();
        return true;
    }

    private int GetCaretIndexFromEditPoint(Point pointer)
    {
        if (!TryGetEditTextLayout(out var editText, out _, out var textOrigin))
        {
            return _editSession.CaretIndex;
        }

        var text = editText.Text ?? string.Empty;
        if (text.Length == 0)
        {
            return 0;
        }

        var bestIndex = 0;
        var bestDistance = double.MaxValue;
        for (var i = 0; i <= text.Length; i++)
        {
            if (!TryGetCaretBounds(editText, textOrigin, i, out var caretBounds))
            {
                continue;
            }

            var dx = Math.Abs(pointer.X - caretBounds.X);
            if (dx < bestDistance)
            {
                bestDistance = dx;
                bestIndex = i;
            }
        }

        return bestIndex;
    }
}
