using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Griddo.Columns;
using Griddo.Editing;
using Griddo.Grid;

namespace GriddoTest.ColumnEdit;

public partial class ColumnEditDialog : Window
{
    private readonly List<IGriddoColumnView> _columnHeaderRegistry = [];

    /// <summary>Fired when Apply is pressed; argument is an ordered snapshot (clones).</summary>
    public event EventHandler<IReadOnlyList<ColumnEditRow>>? PreviewApply;

    /// <summary>Grid whose columns this dialog edits (for nested “Choose columns…” from the preview grid).</summary>
    public global::Griddo.Grid.Griddo? TargetSourceGrid { get; set; }

    /// <summary>Applies current rows and frozen counts to the grid that opened the dialog.</summary>
    public Action<IReadOnlyList<ColumnEditRow>, int, int>? ApplyToSourceGrid { get; set; }

    /// <summary>Host hook for column header context menu (e.g. <see cref="MainWindow"/> demo menu).</summary>
    public Action<global::Griddo.Grid.Griddo, GriddoColumnHeaderMouseEventArgs>? ColumnHeaderMenuHandler { get; set; }

    public IReadOnlyList<IGriddoColumnView> ColumnHeaderRegistry => _columnHeaderRegistry;

    public ColumnEditDialog(IReadOnlyList<ColumnEditRow> templateRows, int initialFrozenColumns, int initialFrozenRows)
    {
        InitializeComponent();
        foreach (var r in templateRows)
        {
            ColumnGrid.Rows.Add(r.Clone());
        }

        BuildColumns();
        FrozenColumnsBox.Text = Math.Clamp(initialFrozenColumns, 0, ColumnGrid.Rows.Count).ToString();
        FrozenRowsBox.Text = initialFrozenRows.ToString();
        AttachUnsignedIntegerOnlyInput(FrozenColumnsBox);
        AttachUnsignedIntegerOnlyInput(FrozenRowsBox);
        ColumnGrid.ColumnHeaderRightClick += ColumnGrid_ColumnHeaderRightClick;
        Closed += (_, _) => ColumnGrid.ColumnHeaderRightClick -= ColumnGrid_ColumnHeaderRightClick;
    }

    /// <summary>Allows only ASCII digits (non‑negative integers); paste inserts digits from clipboard.</summary>
    private static void AttachUnsignedIntegerOnlyInput(TextBox textBox)
    {
        textBox.PreviewTextInput += (_, e) =>
        {
            if (!e.Text.All(static c => c is >= '0' and <= '9'))
            {
                e.Handled = true;
            }
        };

        textBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Space)
            {
                e.Handled = true;
            }
        };

        CommandManager.AddPreviewExecutedHandler(
            textBox,
            (sender, e) =>
            {
                if (e.Command != ApplicationCommands.Paste || sender is not TextBox box)
                {
                    return;
                }

                if (!Clipboard.ContainsText())
                {
                    return;
                }

                var digits = string.Concat(Clipboard.GetText().Where(static c => c is >= '0' and <= '9'));
                e.Handled = true;
                if (digits.Length == 0)
                {
                    return;
                }

                var start = box.SelectionStart;
                var selLen = box.SelectionLength;
                var left = Math.Clamp(start, 0, box.Text.Length);
                var removeEnd = Math.Clamp(left + selLen, left, box.Text.Length);
                box.Text = string.Concat(box.Text.AsSpan(0, left), digits, box.Text.AsSpan(removeEnd));
                box.CaretIndex = left + digits.Length;
                box.SelectionLength = 0;
            });
    }

    private void ColumnGrid_ColumnHeaderRightClick(object? sender, GriddoColumnHeaderMouseEventArgs e)
    {
        ColumnHeaderMenuHandler?.Invoke(ColumnGrid, e);
    }

    public IReadOnlyList<ColumnEditRow>? ResultRows { get; private set; }

    public int ResultFrozenColumns { get; private set; }

    public int ResultFrozenRows { get; private set; }

    private void BuildColumns()
    {
        ColumnGrid.Columns.Clear();
        _columnHeaderRegistry.Clear();

        void AddColumn(IGriddoColumnView columnView)
        {
            ColumnGrid.Columns.Add(columnView);
            _columnHeaderRegistry.Add(columnView);
        }

        AddColumn(new GriddoBoolColumnView(
            "Vis",
            44,
            r => ((ColumnEditRow)r).Visible,
            (r, v) =>
            {
                if (v is not bool b)
                {
                    return false;
                }

                ((ColumnEditRow)r).Visible = b;
                return true;
            }));
        AddColumn(new GriddoBoolColumnView(
            "Fill",
            44,
            r => ((ColumnEditRow)r).Fill,
            (r, v) =>
            {
                if (v is not bool b)
                {
                    return false;
                }

                ((ColumnEditRow)r).Fill = b;
                return true;
            }));
        AddColumn(new GriddoColumnView(
            "Width",
            72,
            r => ((ColumnEditRow)r).Width,
            (r, v) =>
            {
                if (v is double d)
                {
                    ((ColumnEditRow)r).Width = Math.Max(28, d);
                    return true;
                }

                if (double.TryParse(v?.ToString(), out var x))
                {
                    ((ColumnEditRow)r).Width = Math.Max(28, x);
                    return true;
                }

                return false;
            },
            GriddoCellEditors.Number,
            TextAlignment.Right));
        AddColumn(new ReadonlyColumn("Property", 140, r => r.PropertyName));
        AddColumn(new GriddoColumnView(
            "Header",
            160,
            r => ((ColumnEditRow)r).Title,
            (r, v) =>
            {
                ((ColumnEditRow)r).Title = v?.ToString() ?? string.Empty;
                return true;
            }));
        AddColumn(new ReadonlyColumn("Description", 260, r => r.Description));
        AddColumn(new ReadonlyColumn("Value", 220, r => r.SampleDisplay));
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ColumnGrid.SelectedCells.Count == 0 && ColumnGrid.CurrentCell.IsValid)
        {
            ColumnGrid.SelectEntireRow(ColumnGrid.CurrentCell.RowIndex);
        }

        _ = ColumnGrid.TryMoveSelectedRowsStep(-1);
    }

    private void MoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ColumnGrid.SelectedCells.Count == 0 && ColumnGrid.CurrentCell.IsValid)
        {
            ColumnGrid.SelectEntireRow(ColumnGrid.CurrentCell.RowIndex);
        }

        _ = ColumnGrid.TryMoveSelectedRowsStep(1);
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!TryCommitFrozenColumns(out var frozenColumns) || !TryCommitFrozenRows(out var frozenRows))
        {
            return;
        }

        var rows = SnapshotRows();
        ApplyToSourceGrid?.Invoke(rows, frozenColumns, frozenRows);
        PreviewApply?.Invoke(this, rows);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!TryCommitFrozenColumns(out var frozenColumns) || !TryCommitFrozenRows(out var frozenRows))
        {
            return;
        }

        var rows = SnapshotRows();
        ApplyToSourceGrid?.Invoke(rows, frozenColumns, frozenRows);
        ResultRows = rows;
        ResultFrozenColumns = frozenColumns;
        ResultFrozenRows = frozenRows;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        DialogResult = false;
    }

    private bool TryCommitFrozenColumns(out int frozenColumns)
    {
        frozenColumns = 0;
        if (!int.TryParse(FrozenColumnsBox.Text.Trim(), out var fc) || fc < 0)
        {
            MessageBox.Show(
                this,
                "Frozen columns must be a non-negative integer.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        frozenColumns = Math.Min(fc, ColumnGrid.Rows.Count);
        return true;
    }

    private bool TryCommitFrozenRows(out int frozenRows)
    {
        frozenRows = 0;
        if (!int.TryParse(FrozenRowsBox.Text.Trim(), out var fr) || fr < 0)
        {
            MessageBox.Show(
                this,
                "Frozen rows must be a non-negative integer.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        frozenRows = fr;
        return true;
    }

    private List<ColumnEditRow> SnapshotRows() =>
        ColumnGrid.Rows.Cast<ColumnEditRow>().Select(static r => r.Clone()).ToList();

    private sealed class ReadonlyColumn : IGriddoColumnView
    {
        private readonly Func<ColumnEditRow, string> _get;

        public ReadonlyColumn(string header, double width, Func<ColumnEditRow, string> get)
        {
            Header = header;
            Width = width;
            _get = get;
        }

        public string Header { get; set; }
        public double Width { get; }
        public bool Fill { get; set; }
        public bool IsHtml => false;
        public TextAlignment ContentAlignment { get; } = TextAlignment.Left;
        public IGriddoCellEditor Editor => GriddoCellEditors.Text;

        public object? GetValue(object rowSource) => _get((ColumnEditRow)rowSource);

        public bool TrySetValue(object rowSource, object? value) => true;

        public string FormatValue(object? value) => value?.ToString() ?? string.Empty;
    }
}
