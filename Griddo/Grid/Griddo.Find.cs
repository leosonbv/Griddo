using System.Windows;
using System.Windows.Controls;
using Griddo.Primitives;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    private bool FindNextMatch(bool forward, bool fromCurrentMatch)
    {
        if (_findMatchedCells.Count == 0 || Rows.Count == 0 || Columns.Count == 0)
        {
            _findMatchCell = new GriddoCellAddress(-1, -1);
            return false;
        }

        var matchedFlat = _findMatchedCells
            .Select(a => (a.RowIndex * Columns.Count) + a.ColumnIndex)
            .OrderBy(i => i)
            .ToList();

        if (matchedFlat.Count == 0)
        {
            _findMatchCell = new GriddoCellAddress(-1, -1);
            return false;
        }

        var selectedFlat = _findMatchCell.IsValid
            ? (_findMatchCell.RowIndex * Columns.Count) + _findMatchCell.ColumnIndex
            : -1;

        var pickedFlat = matchedFlat[0];
        if (!fromCurrentMatch || selectedFlat < 0)
        {
            pickedFlat = forward ? matchedFlat[0] : matchedFlat[^1];
        }
        else
        {
            if (forward)
            {
                pickedFlat = matchedFlat.FirstOrDefault(i => i > selectedFlat);
                if (pickedFlat == 0 && !matchedFlat.Contains(0))
                {
                    pickedFlat = matchedFlat[0];
                }
            }
            else
            {
                pickedFlat = matchedFlat.LastOrDefault(i => i < selectedFlat);
                if (pickedFlat == 0 && !matchedFlat.Contains(0))
                {
                    pickedFlat = matchedFlat[^1];
                }
            }
        }

        _findMatchCell = new GriddoCellAddress(pickedFlat / Columns.Count, pickedFlat % Columns.Count);
        _currentCell = _findMatchCell;
        CenterCellInViewport(_findMatchCell);
        return true;
    }

    private void RebuildFindMatches()
    {
        _findMatchedCells.Clear();
        if (Rows.Count == 0 || Columns.Count == 0 || string.IsNullOrWhiteSpace(_findText))
        {
            _findMatchCell = new GriddoCellAddress(-1, -1);
            return;
        }

        var normalizedNeedle = _findText.Trim();
        for (var row = 0; row < Rows.Count; row++)
        {
            for (var col = 0; col < Columns.Count; col++)
            {
                var text = GetCellFindText(row, col);
                if (text.IndexOf(normalizedNeedle, StringComparison.CurrentCultureIgnoreCase) >= 0)
                {
                    _findMatchedCells.Add(new GriddoCellAddress(row, col));
                }
            }
        }

        if (_findMatchCell.IsValid && !_findMatchedCells.Contains(_findMatchCell))
        {
            _findMatchCell = new GriddoCellAddress(-1, -1);
        }
    }

    private bool TryPromptFindText(out string findText)
    {
        var owner = Window.GetWindow(this);
        return FindTextDialog.TryShow(owner, _findHistory, _findText, out findText);
    }

    private void AddFindHistory(string findText)
    {
        if (string.IsNullOrWhiteSpace(findText))
        {
            return;
        }

        _findHistory.RemoveAll(x => string.Equals(x, findText, StringComparison.CurrentCulture));
        _findHistory.Insert(0, findText);
        const int maxHistory = 12;
        if (_findHistory.Count > maxHistory)
        {
            _findHistory.RemoveRange(maxHistory, _findHistory.Count - maxHistory);
        }
    }

    private string GetCellFindText(int row, int col)
    {
        if (row < 0 || row >= Rows.Count || col < 0 || col >= Columns.Count)
        {
            return string.Empty;
        }

        var column = Columns[col];
        var value = column.GetValue(Rows[row]);
        return column.FormatValue(value) ?? string.Empty;
    }

    private void CenterCellInViewport(GriddoCellAddress cell)
    {
        if (!cell.IsValid || Rows.Count == 0 || Columns.Count == 0 || _viewportBodyWidth <= 0 || _viewportBodyHeight <= 0)
        {
            return;
        }

        var rect = GetCellRect(cell.RowIndex, cell.ColumnIndex);
        if (rect.IsEmpty)
        {
            return;
        }

        // Center within the vertically scrollable band (below frozen rows), not the full body height.
        var fRows = GetEffectiveFixedRowCount();
        if (cell.RowIndex >= fRows)
        {
            var vh = GetScrollRowsViewportHeight();
            if (vh > 1e-6)
            {
                var targetCenterY = ScaledColumnHeaderHeight + GetFixedRowsHeight() + vh / 2.0;
                var deltaY = rect.Y + (rect.Height / 2.0) - targetCenterY;
                SetVerticalOffset(_verticalOffset + deltaY);
            }
        }

        // Center within the horizontally scrollable band (to the right of frozen columns).
        if (cell.ColumnIndex >= _fixedColumnCount)
        {
            var fixedW = GetFixedColumnsWidth();
            var scrollVp = GetScrollViewportWidth();
            if (scrollVp > 1e-6)
            {
                var targetCenterX = _rowHeaderWidth + fixedW + scrollVp / 2.0;
                var deltaX = rect.X + (rect.Width / 2.0) - targetCenterX;
                SetHorizontalOffset(_horizontalOffset + deltaX);
            }
        }
    }

    private sealed class FindTextDialog : Window
    {
        private readonly ComboBox _input;

        private FindTextDialog(IEnumerable<string> history, string currentValue)
        {
            Title = "Find";
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Width = 360;
            Height = 142;

            var root = new System.Windows.Controls.Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            root.Children.Add(new TextBlock
            {
                Text = "Find text:",
                Margin = new Thickness(0, 0, 0, 6)
            });

            _input = new ComboBox
            {
                IsEditable = true,
                IsTextSearchEnabled = false,
                StaysOpenOnEdit = true,
                Margin = new Thickness(0, 0, 0, 12)
            };
            foreach (var entry in history)
            {
                _input.Items.Add(entry);
            }

            _input.Text = currentValue ?? string.Empty;
            System.Windows.Controls.Grid.SetRow(_input, 1);
            root.Children.Add(_input);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var ok = new Button
            {
                Content = "OK",
                MinWidth = 72,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            ok.Click += (_, _) => DialogResult = true;
            buttons.Children.Add(ok);

            var cancel = new Button
            {
                Content = "Cancel",
                MinWidth = 72,
                IsCancel = true
            };
            buttons.Children.Add(cancel);
            System.Windows.Controls.Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            Content = root;
            Loaded += (_, _) =>
            {
                _input.Focus();
                _input.IsDropDownOpen = _input.Items.Count > 0;
                if (_input.Template.FindName("PART_EditableTextBox", _input) is TextBox tb)
                {
                    tb.SelectAll();
                }
            };
        }

        public static bool TryShow(Window? owner, IEnumerable<string> history, string currentValue, out string findText)
        {
            var dlg = new FindTextDialog(history, currentValue) { Owner = owner };
            var result = dlg.ShowDialog() == true;
            findText = (dlg._input.Text ?? string.Empty).Trim();
            return result && !string.IsNullOrWhiteSpace(findText);
        }
    }
}
