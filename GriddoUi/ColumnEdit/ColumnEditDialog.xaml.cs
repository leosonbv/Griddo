using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Griddo.Columns;
using Griddo.Editing;
using Griddo.Grid;
using WpfColorFontDialog;

namespace GriddoUi.ColumnEdit;

public partial class GridConfigurator : Window
{
    private readonly List<IGriddoColumnView> _columnHeaderRegistry = [];
    private readonly List<IGriddoColumnView> _generalColumnHeaderRegistry = [];
    private int _valueColumnIndex = -1;

    /// <summary>Fired when Apply is pressed; argument is an ordered snapshot (clones).</summary>
    public event EventHandler<IReadOnlyList<ColumnEditRow>>? PreviewApply;

    /// <summary>Grid whose columns this dialog edits (for nested "Grid configurator..." from the preview grid).</summary>
    public global::Griddo.Grid.Griddo? TargetSourceGrid { get; set; }

    /// <summary>Applies current rows, frozen counts, and general options to the grid that opened the dialog.</summary>
    public Action<IReadOnlyList<ColumnEditRow>, int, int, ColumnChooserGeneralOptions>? ApplyToSourceGrid { get; set; }

    /// <summary>Host hook for column header context menu (e.g. <see cref="MainWindow"/> demo menu).</summary>
    public Action<global::Griddo.Grid.Griddo, IReadOnlyList<IGriddoColumnView>, GriddoColumnHeaderMouseEventArgs>? ColumnHeaderMenuHandler { get; set; }

    public IReadOnlyList<IGriddoColumnView> ColumnHeaderRegistry => _columnHeaderRegistry;
    public IReadOnlyList<IGriddoColumnView> GeneralColumnHeaderRegistry => _generalColumnHeaderRegistry;
    public global::Griddo.Grid.Griddo ConfigColumnsGrid => ColumnGrid;
    public global::Griddo.Grid.Griddo ConfigGeneralSettingsGrid => GeneralPropertyGrid;

    public GridConfigurator(
        IReadOnlyList<ColumnEditRow> templateRows,
        int initialFrozenColumns,
        int initialFrozenRows,
        ColumnChooserGeneralOptions? initialOptions = null)
    {
        InitializeComponent();
        foreach (var r in templateRows)
        {
            ColumnGrid.Rows.Add(r.Clone());
        }

        BuildColumns();
        var options = initialOptions ?? new ColumnChooserGeneralOptions();
        BuildGeneralPropertyGrid(options, initialFrozenColumns, initialFrozenRows);
        ColumnGrid.CellPropertyViewResolver = ResolveCellPropertyViewForConfigurator;
        ColumnGrid.ColumnHeaderRightClick += ColumnGrid_ColumnHeaderRightClick;
        GeneralPropertyGrid.ColumnHeaderRightClick += GeneralPropertyGrid_ColumnHeaderRightClick;
        Closed += (_, _) =>
        {
            ColumnGrid.ColumnHeaderRightClick -= ColumnGrid_ColumnHeaderRightClick;
            GeneralPropertyGrid.ColumnHeaderRightClick -= GeneralPropertyGrid_ColumnHeaderRightClick;
        };
    }

    private void BuildGeneralPropertyGrid(ColumnChooserGeneralOptions options, int frozenColumns, int frozenRows)
    {
        _generalColumnHeaderRegistry.Clear();
        GeneralPropertyGrid.Columns.Clear();
        GeneralPropertyGrid.Rows.Clear();
        var settingColumn = new GriddoColumnView(
            "Setting",
            280,
            static row => ((GeneralSettingRow)row).DisplayName,
            static (_, _) => false,
            GriddoCellEditors.Text,
            TextAlignment.Left,
            fill: false);
        var valueColumn = new GeneralSettingValueColumnView("Value", 120);
        GeneralPropertyGrid.Columns.Add(settingColumn);
        GeneralPropertyGrid.Columns.Add(valueColumn);
        _generalColumnHeaderRegistry.Add(settingColumn);
        _generalColumnHeaderRegistry.Add(valueColumn);

        var fc = Math.Clamp(frozenColumns, 0, ColumnGrid.Rows.Count);
        GeneralPropertyGrid.Rows.Add(
            new GeneralSettingRow(
                GeneralSettingKind.RowHeight,
                GeneralSettingValueKind.UnsignedInt,
                "Row height",
                options.RowHeight));
        GeneralPropertyGrid.Rows.Add(
            new GeneralSettingRow(
                GeneralSettingKind.FillRowsVisibleCount,
                GeneralSettingValueKind.UnsignedInt,
                "Fill rows (visible row count, 0–10)",
                options.VisibleRowCount));
        GeneralPropertyGrid.Rows.Add(
            new GeneralSettingRow(GeneralSettingKind.FrozenColumns, GeneralSettingValueKind.UnsignedInt, "Frozen columns", fc));
        GeneralPropertyGrid.Rows.Add(
            new GeneralSettingRow(GeneralSettingKind.FrozenRows, GeneralSettingValueKind.UnsignedInt, "Frozen rows", Math.Max(0, frozenRows)));
        GeneralPropertyGrid.Rows.Add(
            new GeneralSettingRow(
                GeneralSettingKind.ImmediatePlottoEdit,
                GeneralSettingValueKind.Boolean,
                "Immediate edit (Plot only)",
                boolValue: options.ImmediatePlottoEdit));
        GeneralPropertyGrid.Rows.Add(
            new GeneralSettingRow(
                GeneralSettingKind.ShowSortingIndicators,
                GeneralSettingValueKind.Boolean,
                "Show sorting indicators",
                boolValue: options.ShowSortingIndicators));
        GeneralPropertyGrid.Rows.Add(
            new GeneralSettingRow(
                GeneralSettingKind.ShowHorizontalScrollBar,
                GeneralSettingValueKind.Boolean,
                "Show horizontal scrollbar",
                boolValue: options.ShowHorizontalScrollBar));
        GeneralPropertyGrid.Rows.Add(
            new GeneralSettingRow(
                GeneralSettingKind.ShowVerticalScrollBar,
                GeneralSettingValueKind.Boolean,
                "Show vertical scrollbar",
                boolValue: options.ShowVerticalScrollBar));
        GeneralPropertyGrid.Rows.Add(
            new GeneralSettingRow(
                GeneralSettingKind.ShowSelectionColor,
                GeneralSettingValueKind.Boolean,
                "Show selection color",
                boolValue: options.ShowSelectionColor));
        GeneralPropertyGrid.Rows.Add(
            new GeneralSettingRow(
                GeneralSettingKind.ShowRowSelectionColor,
                GeneralSettingValueKind.Boolean,
                "Show row header selection color",
                boolValue: options.ShowRowSelectionColor));
        GeneralPropertyGrid.Rows.Add(
            new GeneralSettingRow(
                GeneralSettingKind.ShowColSelectionColor,
                GeneralSettingValueKind.Boolean,
                "Show column header selection color",
                boolValue: options.ShowColSelectionColor));
        GeneralPropertyGrid.Rows.Add(
            new GeneralSettingRow(
                GeneralSettingKind.ShowCurrentCellRect,
                GeneralSettingValueKind.Boolean,
                "Show current cell rectangle",
                boolValue: options.ShowCurrentCellRect));
        GeneralPropertyGrid.Rows.Add(
            new GeneralSettingRow(
                GeneralSettingKind.ShowEditCellRect,
                GeneralSettingValueKind.Boolean,
                "Show edit cell rectangle",
                boolValue: options.ShowEditCellRect));
    }

    private GeneralSettingRow? GeneralRow(GeneralSettingKind kind) =>
        GeneralPropertyGrid.Rows.OfType<GeneralSettingRow>().FirstOrDefault(r => r.Setting == kind);

    private void ColumnGrid_ColumnHeaderRightClick(object? sender, GriddoColumnHeaderMouseEventArgs e)
    {
        ColumnHeaderMenuHandler?.Invoke(ColumnGrid, _columnHeaderRegistry, e);
    }

    private void GeneralPropertyGrid_ColumnHeaderRightClick(object? sender, GriddoColumnHeaderMouseEventArgs e)
    {
        ColumnHeaderMenuHandler?.Invoke(GeneralPropertyGrid, _generalColumnHeaderRegistry, e);
    }

    public IReadOnlyList<ColumnEditRow>? ResultRows { get; private set; }

    public int ResultFrozenColumns { get; private set; }

    public int ResultFrozenRows { get; private set; }

    public ColumnChooserGeneralOptions ResultGeneralOptions { get; private set; } = new();

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
        AddColumn(new GriddoColumnView(
            "Font",
            220,
            r => FormatFontSummary((ColumnEditRow)r),
            (r, v) =>
            {
                if (!string.Equals(v?.ToString(), "...", StringComparison.Ordinal))
                {
                    return false;
                }

                var baseRow = (ColumnEditRow)r;
                var targets = ResolveTargetsForEdit(baseRow);
                OpenFontEditor(targets);
                return true;
            },
            GriddoCellEditors.DialogLauncher));
        AddColumn(new GriddoColumnView(
            "Back color",
            130,
            r => FormatOneColor(((ColumnEditRow)r).BackgroundColor),
            (r, v) =>
            {
                ((ColumnEditRow)r).BackgroundColor = NormalizeBackColor(v?.ToString());
                return true;
            },
            GriddoCellEditors.KnownColorsDropdown));
        AddColumn(new GriddoColumnView(
            "Format",
            140,
            r => ((ColumnEditRow)r).FormatString,
            (r, v) =>
            {
                if (!TryFormatValue(v?.ToString(), out var normalized))
                {
                    return false;
                }

                ((ColumnEditRow)r).FormatString = normalized;
                return true;
            },
            GriddoCellEditors.FormatStringOptions));
        AddColumn(new GriddoColumnView(
            "Sort#",
            58,
            r => ((ColumnEditRow)r).SortPriority,
            (r, v) =>
            {
                if (v is double d)
                {
                    ((ColumnEditRow)r).SortPriority = Math.Max(0, (int)Math.Round(d));
                    return true;
                }

                if (int.TryParse(v?.ToString(), out var x))
                {
                    ((ColumnEditRow)r).SortPriority = Math.Max(0, x);
                    return true;
                }

                return false;
            },
            GriddoCellEditors.Number,
            TextAlignment.Right));
        AddColumn(new GriddoBoolColumnView(
            "Asc",
            48,
            r => ((ColumnEditRow)r).SortAscending,
            (r, v) =>
            {
                if (v is not bool b)
                {
                    return false;
                }

                ((ColumnEditRow)r).SortAscending = b;
                return true;
            }));
        AddColumn(new ReadonlyColumn("Source", 120, r => r.SourceObjectName));
        AddColumn(new ReadonlyColumn("Property", 140, r => r.PropertyName));
        AddColumn(new GriddoColumnView(
            "Header",
            150,
            r => ((ColumnEditRow)r).Title,
            (r, v) =>
            {
                ((ColumnEditRow)r).Title = v?.ToString() ?? string.Empty;
                return true;
            }));
        AddColumn(new GriddoColumnView(
            "Abbrev",
            100,
            r => ((ColumnEditRow)r).AbbreviatedTitle,
            (r, v) =>
            {
                ((ColumnEditRow)r).AbbreviatedTitle = v?.ToString() ?? string.Empty;
                return true;
            }));
        AddColumn(new GriddoColumnView(
            "Description",
            260,
            r => ((ColumnEditRow)r).Description,
            (r, v) =>
            {
                ((ColumnEditRow)r).Description = v?.ToString() ?? string.Empty;
                return true;
            }));
        _valueColumnIndex = ColumnGrid.Columns.Count;
        AddColumn(new ValuePreviewColumn("Value", 220));
    }

    private GriddoCellPropertyView? ResolveCellPropertyViewForConfigurator(object rowSource, int columnIndex)
    {
        if (columnIndex != _valueColumnIndex || rowSource is not ColumnEditRow row)
        {
            return null;
        }

        return new GriddoCellPropertyView
        {
            FormatString = row.FormatString ?? string.Empty,
            FontFamilyName = row.FontFamilyName ?? string.Empty,
            FontSize = row.FontSize,
            FontStyleName = row.FontStyleName ?? string.Empty,
            ForegroundColor = row.ForegroundColor ?? string.Empty,
            BackgroundColor = row.BackgroundColor ?? string.Empty
        };
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
        if (!TryCommitFrozenColumns(out var frozenColumns)
            || !TryCommitFrozenRows(out var frozenRows)
            || !TryCommitRowHeight(out _)
            || !TryCommitVisibleRows(out _))
        {
            return;
        }

        var rows = SnapshotRows();
        var generalOptions = BuildGeneralOptions();
        ApplyToSourceGrid?.Invoke(rows, frozenColumns, frozenRows, generalOptions);
        PreviewApply?.Invoke(this, rows);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!TryCommitFrozenColumns(out var frozenColumns)
            || !TryCommitFrozenRows(out var frozenRows)
            || !TryCommitRowHeight(out _)
            || !TryCommitVisibleRows(out _))
        {
            return;
        }

        var rows = SnapshotRows();
        var generalOptions = BuildGeneralOptions();
        ApplyToSourceGrid?.Invoke(rows, frozenColumns, frozenRows, generalOptions);
        ResultRows = rows;
        ResultFrozenColumns = frozenColumns;
        ResultFrozenRows = frozenRows;
        ResultGeneralOptions = generalOptions.Clone();
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
        var row = GeneralRow(GeneralSettingKind.FrozenColumns);
        if (row is null)
        {
            return false;
        }

        var fc = row.IntValue;
        if (fc < 0)
        {
            System.Windows.MessageBox.Show(
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
        var row = GeneralRow(GeneralSettingKind.FrozenRows);
        if (row is null)
        {
            return false;
        }

        var fr = row.IntValue;
        if (fr < 0)
        {
            System.Windows.MessageBox.Show(
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

    private bool TryCommitVisibleRows(out int visibleRows)
    {
        visibleRows = 0;
        var row = GeneralRow(GeneralSettingKind.FillRowsVisibleCount);
        if (row is null)
        {
            return false;
        }

        var vr = row.IntValue;
        if (vr < 0 || vr > 10)
        {
            System.Windows.MessageBox.Show(
                this,
                "Visible rows must be an integer between 0 and 10.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        visibleRows = vr;
        return true;
    }

    private bool TryCommitRowHeight(out int rowHeight)
    {
        rowHeight = 24;
        var row = GeneralRow(GeneralSettingKind.RowHeight);
        if (row is null)
        {
            return false;
        }

        var rh = row.IntValue;
        if (rh < 18 || rh > 400)
        {
            System.Windows.MessageBox.Show(
                this,
                "Row height must be an integer between 18 and 400.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        rowHeight = rh;
        return true;
    }

    private ColumnChooserGeneralOptions BuildGeneralOptions()
    {
        _ = TryCommitRowHeight(out var rowHeight);
        _ = TryCommitVisibleRows(out var visibleRows);
        return new ColumnChooserGeneralOptions
        {
            RowHeight = rowHeight,
            VisibleRowCount = visibleRows,
            ShowSelectionColor = GeneralRow(GeneralSettingKind.ShowSelectionColor)?.BoolValue ?? true,
            ShowCurrentCellRect = GeneralRow(GeneralSettingKind.ShowCurrentCellRect)?.BoolValue ?? true,
            ShowRowSelectionColor = GeneralRow(GeneralSettingKind.ShowRowSelectionColor)?.BoolValue ?? true,
            ShowColSelectionColor = GeneralRow(GeneralSettingKind.ShowColSelectionColor)?.BoolValue ?? true,
            ShowEditCellRect = GeneralRow(GeneralSettingKind.ShowEditCellRect)?.BoolValue ?? true,
            ShowSortingIndicators = GeneralRow(GeneralSettingKind.ShowSortingIndicators)?.BoolValue ?? true,
            ShowHorizontalScrollBar = GeneralRow(GeneralSettingKind.ShowHorizontalScrollBar)?.BoolValue ?? true,
            ShowVerticalScrollBar = GeneralRow(GeneralSettingKind.ShowVerticalScrollBar)?.BoolValue ?? true,
            ImmediatePlottoEdit = GeneralRow(GeneralSettingKind.ImmediatePlottoEdit)?.BoolValue ?? false
        };
    }

    private List<ColumnEditRow> SnapshotRows() =>
        ColumnGrid.Rows.Cast<ColumnEditRow>().Select(static r => r.Clone()).ToList();

    private List<ColumnEditRow> GetSelectedEditorRows()
    {
        var selectedIndices = ColumnGrid.SelectedCells
            .Select(c => c.RowIndex)
            .Where(i => i >= 0 && i < ColumnGrid.Rows.Count)
            .Distinct()
            .OrderBy(i => i)
            .ToList();

        if (selectedIndices.Count == 0 && ColumnGrid.CurrentCell.IsValid)
        {
            selectedIndices.Add(ColumnGrid.CurrentCell.RowIndex);
        }

        return selectedIndices
            .Where(i => i >= 0 && i < ColumnGrid.Rows.Count)
            .Select(i => ColumnGrid.Rows[i] as ColumnEditRow)
            .Where(r => r is not null)
            .Cast<ColumnEditRow>()
            .ToList();
    }

    private List<ColumnEditRow> ResolveTargetsForEdit(ColumnEditRow row)
    {
        var selected = GetSelectedEditorRows();
        if (selected.Any(r => ReferenceEquals(r, row)))
        {
            return selected;
        }

        return [row];
    }

    private void OpenFontEditor(IReadOnlyList<ColumnEditRow> targets)
    {
        if (targets.Count == 0)
        {
            return;
        }

        var seed = targets[0];
        var dialog = new ColorFontDialog(
            previewFontInFontList: true,
            allowArbitraryFontSizes: true,
            showColorPicker: true)
        {
            Font = BuildDialogFontInfo(seed, seed.ForegroundColor)
        };

        if (dialog.ShowDialog() != true || dialog.Font is null)
        {
            return;
        }

        var style = FormatFontStyle(dialog.Font.Style, dialog.Font.Weight, HasUnderlineStyle(seed.FontStyleName));
        foreach (var row in targets)
        {
            row.FontFamilyName = dialog.Font.Family?.Source ?? row.FontFamilyName;
            row.FontSize = Math.Max(6, dialog.Font.Size);
            row.FontStyleName = style;
            if (dialog.Font.BrushColor is SolidColorBrush solidBrush)
            {
                row.ForegroundColor = ToHexColor(solidBrush);
            }
        }

        ColumnGrid.InvalidateVisual();
    }

    private static FontInfo BuildDialogFontInfo(ColumnEditRow row, string colorText)
    {
        var familyName = string.IsNullOrWhiteSpace(row.FontFamilyName) ? "Segoe UI" : row.FontFamilyName;
        var family = new FontFamily(familyName);
        var (style, weight, _) = ParseFontTraits(row.FontStyleName);
        return new FontInfo
        {
            Family = family,
            Size = Math.Max(6, row.FontSize <= 0 ? 12 : row.FontSize),
            Style = style,
            Stretch = FontStretches.Normal,
            Weight = weight,
            BrushColor = ParseSolidColorBrush(colorText)
        };
    }

    private static SolidColorBrush ParseSolidColorBrush(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Brushes.Black;
        }

        try
        {
            if (new BrushConverter().ConvertFromString(text) is SolidColorBrush solid)
            {
                return solid;
            }

            return Brushes.Black;
        }
        catch
        {
            return Brushes.Black;
        }
    }

    private static string FormatFontSummary(ColumnEditRow row)
    {
        var family = string.IsNullOrWhiteSpace(row.FontFamilyName) ? "(default)" : row.FontFamilyName;
        var size = row.FontSize > 0 ? row.FontSize.ToString("0.#") : "default";
        var style = string.IsNullOrWhiteSpace(row.FontStyleName) ? "Regular" : row.FontStyleName;
        var fg = string.IsNullOrWhiteSpace(row.ForegroundColor) ? "(default)" : row.ForegroundColor;
        return $"{family}, {size}, {style}, Fg:{fg}";
    }

    private static string FormatOneColor(string color)
    {
        return string.IsNullOrWhiteSpace(color) ? "(default)" : color;
    }

    private static string NormalizeBackColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "(default)", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return value.Trim();
    }

    private static bool TryFormatValue(string? value, out string normalized)
    {
        normalized = string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "(none)", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : value.Trim();

        if (normalized.Length == 0)
        {
            return true;
        }

        var numberValid = true;
        try
        {
            _ = 12345.6789.ToString(normalized, System.Globalization.CultureInfo.CurrentCulture);
        }
        catch (FormatException)
        {
            numberValid = false;
        }

        var dateValid = true;
        try
        {
            _ = DateTime.Now.ToString(normalized, System.Globalization.CultureInfo.CurrentCulture);
        }
        catch (FormatException)
        {
            dateValid = false;
        }

        return numberValid || dateValid;
    }

    private static bool HasUnderlineStyle(string style)
    {
        var normalized = (style ?? string.Empty).ToLowerInvariant();
        return normalized.Contains("underline", StringComparison.Ordinal);
    }

    private static (FontStyle style, FontWeight weight, bool underline) ParseFontTraits(string styleText)
    {
        var normalized = (styleText ?? string.Empty).ToLowerInvariant();
        var style = normalized.Contains("italic", StringComparison.Ordinal)
            ? FontStyles.Italic
            : FontStyles.Normal;
        var weight = normalized.Contains("bold", StringComparison.Ordinal)
            ? FontWeights.Bold
            : FontWeights.Normal;
        var underline = normalized.Contains("underline", StringComparison.Ordinal);
        return (style, weight, underline);
    }

    private static string FormatFontStyle(FontStyle style, FontWeight weight, bool underline)
    {
        var parts = new List<string>();
        if (style == FontStyles.Italic)
        {
            parts.Add("Italic");
        }

        if (weight == FontWeights.Bold)
        {
            parts.Add("Bold");
        }

        if (underline)
        {
            parts.Add("Underline");
        }

        return parts.Count == 0 ? string.Empty : string.Join(", ", parts);
    }

    private static string ToHexColor(SolidColorBrush brush)
    {
        var c = brush.Color;
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private sealed class ReadonlyColumn : IGriddoColumnView
    {
        private readonly Func<ColumnEditRow, object?> _get;

        public ReadonlyColumn(string header, double width, Func<ColumnEditRow, object?> get)
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

    private sealed class ValuePreviewColumn(string header, double width) : IGriddoColumnView, IGriddoHostedColumnView
    {
        public string Header { get; set; } = header;
        public double Width { get; } = width;
        public bool Fill { get; set; }
        public bool IsHtml => false;
        public TextAlignment ContentAlignment { get; } = TextAlignment.Left;
        public IGriddoCellEditor Editor => GriddoCellEditors.Text;

        public object? GetValue(object rowSource) => rowSource is ColumnEditRow row ? row.SampleValue ?? row.SampleDisplay : string.Empty;
        public bool TrySetValue(object rowSource, object? value) => true;
        public string FormatValue(object? value) => value?.ToString() ?? string.Empty;

        public FrameworkElement CreateHostElement() => new ContentControl();

        public void UpdateHostElement(FrameworkElement host, object rowSource, bool isSelected, bool isCurrentCell)
        {
            if (host is not ContentControl content || rowSource is not ColumnEditRow row)
            {
                return;
            }

            if (row.SourceColumnView is IGriddoHostedColumnView hostedSource && row.SampleRowSource is not null)
            {
                var active = content.Content as FrameworkElement;
                if (active is null || !ReferenceEquals(content.Tag, hostedSource))
                {
                    active = hostedSource.CreateHostElement();
                    content.Content = active;
                    content.Tag = hostedSource;
                }

                hostedSource.UpdateHostElement(active, row.SampleRowSource, isSelected, isCurrentCell);
                return;
            }

            var textBlock = content.Content as TextBlock;
            if (textBlock is null)
            {
                textBlock = new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                content.Content = textBlock;
                content.Tag = null;
            }

            textBlock.Text = FormatSample(row);
            textBlock.FontSize = row.FontSize > 0 ? row.FontSize : 12;
            if (!string.IsNullOrWhiteSpace(row.FontFamilyName))
            {
                try
                {
                    textBlock.FontFamily = new FontFamily(row.FontFamilyName);
                }
                catch
                {
                    // Keep default font.
                }
            }

            var normalizedStyle = NormalizeStyle(row.FontStyleName);
            textBlock.FontStyle = normalizedStyle.Contains("italic", StringComparison.Ordinal) ? FontStyles.Italic : FontStyles.Normal;
            textBlock.FontWeight = normalizedStyle.Contains("bold", StringComparison.Ordinal) ? FontWeights.Bold : FontWeights.Normal;
            textBlock.TextDecorations = normalizedStyle.Contains("underline", StringComparison.Ordinal) ? TextDecorations.Underline : null;
            textBlock.Foreground = TryBrush(row.ForegroundColor) ?? Brushes.Black;
            content.Background = TryBrush(row.BackgroundColor);
        }

        public bool IsHostInEditMode(FrameworkElement host)
        {
            _ = host;
            return false;
        }

        public void SetHostEditMode(FrameworkElement host, bool isEditing)
        {
            _ = host;
            _ = isEditing;
        }

        public bool TryGetClipboardHtmlFragment(FrameworkElement? host, object rowSource, int cellWidthPx, int cellHeightPx, out string htmlFragment)
        {
            _ = host;
            _ = rowSource;
            _ = cellWidthPx;
            _ = cellHeightPx;
            htmlFragment = string.Empty;
            return false;
        }

        public void SyncHostedUiScale(FrameworkElement host, double contentScale)
        {
            _ = host;
            _ = contentScale;
        }

        private static Brush? TryBrush(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                return new BrushConverter().ConvertFromString(text) as Brush;
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeStyle(string style)
        {
            if (string.IsNullOrWhiteSpace(style))
            {
                return string.Empty;
            }

            return style
                .Trim()
                .Replace("-", " ", StringComparison.Ordinal)
                .Replace("_", " ", StringComparison.Ordinal)
                .ToLowerInvariant();
        }

        private static string FormatSample(ColumnEditRow row)
        {
            if (row.SampleValue is null)
            {
                return row.SampleDisplay;
            }

            if (!string.IsNullOrWhiteSpace(row.FormatString) && row.SampleValue is IFormattable formattable)
            {
                try
                {
                    return formattable.ToString(row.FormatString, CultureInfo.CurrentCulture);
                }
                catch (FormatException)
                {
                    // Fall through.
                }
            }

            return row.SampleValue switch
            {
                string text => text,
                IFormattable fmt => fmt.ToString(null, CultureInfo.CurrentCulture),
                _ => row.SampleValue.ToString() ?? string.Empty
            };
        }
    }

}
