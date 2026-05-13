using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Griddo.Fields;
using Griddo.Editing;
using Griddo.Primitives;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    /// <summary>Hosted plot/chart cells stay interactive regardless of <see cref="IGriddoFieldEditableHeaderView.AllowCellEdit"/>.</summary>
    private static bool FieldAllowsCellEdit(IGriddoFieldView field) =>
        field is IGriddoHostedFieldView
        || field is not IGriddoFieldEditableHeaderView h
        || h.AllowCellEdit;

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
        if (_currentCell.RecordIndex < 0 || _currentCell.RecordIndex >= Records.Count || _currentCell.FieldIndex < 0 || _currentCell.FieldIndex >= Fields.Count)
        {
            return false;
        }

        if (Fields[_currentCell.FieldIndex] is not IGriddoHostedFieldView hostedField)
        {
            return false;
        }

        return TryGetHostedElement(_currentCell) is { } host && hostedField.IsHostInEditMode(host);
    }

    private bool IsHostedCellInEditMode(GriddoCellAddress cell)
    {
        if (cell.RecordIndex < 0 || cell.RecordIndex >= Records.Count || cell.FieldIndex < 0 || cell.FieldIndex >= Fields.Count)
        {
            return false;
        }

        if (Fields[cell.FieldIndex] is not IGriddoHostedFieldView hostedField)
        {
            return false;
        }

        return TryGetHostedElement(cell) is { } host && hostedField.IsHostInEditMode(host);
    }

    private void SetCurrentHostedCellEditMode(bool isEditing)
    {
        if (_currentCell.RecordIndex < 0 || _currentCell.RecordIndex >= Records.Count || _currentCell.FieldIndex < 0 || _currentCell.FieldIndex >= Fields.Count)
        {
            return;
        }

        if (Fields[_currentCell.FieldIndex] is not IGriddoHostedFieldView hostedField)
        {
            return;
        }

        if (TryGetHostedElement(_currentCell) is not { } host)
        {
            return;
        }

        hostedField.SetHostEditMode(host, isEditing);
        InvalidateVisual();
    }

    private void BeginCurrentCellEdit()
    {
        if (_currentCell.RecordIndex < 0 || _currentCell.RecordIndex >= Records.Count || _currentCell.FieldIndex < 0 || _currentCell.FieldIndex >= Fields.Count)
        {
            return;
        }

        var field = Fields[_currentCell.FieldIndex];
        if (field is IGriddoHostedFieldView)
        {
            SetCurrentHostedCellEditMode(true);
            return;
        }

        if (!FieldAllowsCellEdit(field))
        {
            return;
        }

        BeginEditWithoutReplacing();
    }


    private bool TryGetCurrentField(out IGriddoFieldView field)
    {
        if (_currentCell.FieldIndex < 0 || _currentCell.FieldIndex >= Fields.Count)
        {
            field = default!;
            return false;
        }

        field = Fields[_currentCell.FieldIndex];
        return true;
    }

    private object? GetCurrentValue()
    {
        if (_currentCell.RecordIndex < 0 || _currentCell.RecordIndex >= Records.Count || !TryGetCurrentField(out var field))
        {
            return null;
        }

        return field.GetValue(Records[_currentCell.RecordIndex]);
    }

    private void BeginEditWithoutReplacing()
    {
        CloseActiveEditOptionsMenu();
        if (!TryGetCurrentField(out var field))
        {
            return;
        }

        if (field is IGriddoHostedFieldView)
        {
            return;
        }

        if (!FieldAllowsCellEdit(field))
        {
            return;
        }

        _editSession.Start(field.Editor.BeginEdit(GetCurrentValue()));
        _isEditing = true;
        if (field.Editor is IGriddoOptionsCellEditor optionsEditor)
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
        if (!_isEditing || !TryGetCurrentField(out var field))
        {
            return;
        }

        if (_currentCell.RecordIndex < 0 || _currentCell.RecordIndex >= Records.Count)
        {
            return;
        }

        _isCommittingEdit = true;
        try
        {
            if (field.Editor.TryCommit(_editSession.Buffer, out var newValue) && FieldAllowsCellEdit(field))
            {
                field.TrySetValue(Records[_currentCell.RecordIndex], newValue);
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

        var cellRect = GetCellRect(_currentCell.RecordIndex, _currentCell.FieldIndex);
        if (cellRect.IsEmpty)
        {
            return;
        }

        CloseActiveEditOptionsMenu();

        var selectedValues = optionsEditor.ParseValues(_editSession.Buffer)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var recordSource = _currentCell.RecordIndex >= 0 && _currentCell.RecordIndex < Records.Count
            ? Records[_currentCell.RecordIndex]
            : null;
        var options = optionsEditor is IGriddoContextualOptionsCellEditor contextualEditor
            ? contextualEditor.GetOptions(recordSource)
            : optionsEditor.Options;
        if (options.Count == 0)
        {
            return;
        }
        var menu = new ContextMenu
        {
            PlacementTarget = this,
            Placement = PlacementMode.RelativePoint,
            HorizontalOffset = cellRect.X + 2,
            VerticalOffset = Math.Min(cellRect.Bottom, ActualHeight - 2)
        };
        ApplyGriddoContextMenuSelectionStyle(menu);

        foreach (var option in options)
        {
            var localOption = option;
            var item = new MenuItem
            {
                Header = BuildOptionsMenuHeader(optionsEditor, recordSource, localOption),
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
                if (_isEditing && TryGetCurrentField(out var currentCol) && ReferenceEquals(currentCol.Editor, optionsEditor))
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

    private static object BuildOptionsMenuHeader(IGriddoOptionsCellEditor optionsEditor, object? recordSource, string option)
    {
        if (optionsEditor is not IGriddoSwatchOptionsCellEditor swatchEditor
            || !swatchEditor.TryGetSwatchBrush(option, out var swatchBrush))
        {
            if (optionsEditor is IGriddoContextualOptionsCellEditor contextualEditor
                && contextualEditor.TryGetOptionExample(recordSource, option, out var example))
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

        if (!_isEditing || !_currentCell.IsValid || !TryGetCurrentField(out var field))
        {
            return false;
        }

        var rect = GetCellRect(_currentCell.RecordIndex, _currentCell.FieldIndex);
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
        editText.TextAlignment = field.ContentAlignment;
        editText.MaxTextWidth = Math.Max(1, editContentRect.Width - 8);
        editText.MaxTextHeight = Math.Max(1, editContentRect.Height - 4);
        editText.Trimming = TextTrimming.CharacterEllipsis;

        var contentWidth = Math.Max(1, editContentRect.Width - 8);
        var totalTextWidth = Math.Min(editText.WidthIncludingTrailingWhitespace, contentWidth);
        var textStartX = editContentRect.X + 4;
        if (field.ContentAlignment == TextAlignment.Right)
        {
            textStartX += Math.Max(0, contentWidth - totalTextWidth);
        }
        else if (field.ContentAlignment == TextAlignment.Center)
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
            && TryGetCurrentField(out var field)
            && field.Editor is IGriddoDialogButtonCellEditor;

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

        var rect = GetCellRect(_currentCell.RecordIndex, _currentCell.FieldIndex);
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
        if (!TryGetCurrentField(out var field)
            || field.Editor is not IGriddoDialogButtonCellEditor dialogEditor)
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
