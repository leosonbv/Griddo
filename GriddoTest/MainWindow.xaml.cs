using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Griddo;
using Griddo.Columns;
using Griddo.Editing;
using Griddo.Grid;
using Plotto.Charting.Controls;
using Plotto.Charting.Core;
using GriddoTest.ColumnEdit;

namespace GriddoTest;

public partial class MainWindow : Window
{
    private const string PlottoColumnHeader = "Plotto Cell";
    private const string CalibrationColumnHeader = "Calibration";
    private readonly List<IGriddoColumnView> _allColumns = [];

    private readonly ChromatogramControl _plotConfigFallback = new()
    {
        AxisLabelX = "Time",
        AxisLabelY = "Intensity",
        ChartTitle = "Chromatogram"
    };

    public MainWindow()
    {
        InitializeComponent();
        ConfigureGrid();
        DemoGrid.ColumnHeaderRightClick += (_, e) => OnColumnHeaderRightClick(DemoGrid, _allColumns, DemoGrid, e);
        DemoGrid.UniformRowHeight = 132;
        DemoGrid.FixedColumnCount = 1;
        DemoGrid.HostedPlotDirectEditOnMouseDown = true;
    }

    private void OnColumnHeaderRightClick(
        global::Griddo.Grid.Griddo targetGrid,
        IReadOnlyList<IGriddoColumnView> columnRegistry,
        global::Griddo.Grid.Griddo sourceGridForChooser,
        GriddoColumnHeaderMouseEventArgs e)
    {
        if (e.ColumnIndex < 0 || e.ColumnIndex >= targetGrid.Columns.Count)
        {
            return;
        }

        var indices = e.SelectedColumnIndices
            .Where(i => i >= 0 && i < targetGrid.Columns.Count)
            .Distinct()
            .OrderBy(i => i)
            .ToList();
        if (indices.Count == 0)
        {
            return;
        }

        var targetColumns = indices.Select(i => targetGrid.Columns[i]).ToList();
        var menu = new ContextMenu
        {
            PlacementTarget = targetGrid,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
        };

        var fillItem = new MenuItem
        {
            Header = targetColumns.Count > 1 ? "Fill selected columns" : "Fill column",
            IsCheckable = true,
            IsChecked = targetColumns.All(c => c.Fill)
        };
        fillItem.Click += (_, _) =>
        {
            foreach (var target in targetColumns)
            {
                target.Fill = fillItem.IsChecked;
            }

            targetGrid.InvalidateMeasure();
            targetGrid.InvalidateVisual();
        };
        menu.Items.Add(fillItem);

        var autoWidthAllColumnsItem = new MenuItem { Header = "Auto width all columns" };
        autoWidthAllColumnsItem.Click += (_, _) => targetGrid.AutoSizeAllColumns();
        menu.Items.Add(autoWidthAllColumnsItem);

        var chooseColumnsItem = new MenuItem { Header = "Choose _columns…" };
        chooseColumnsItem.Click += (_, _) => OpenChooseColumnsDialog(sourceGridForChooser);
        menu.Items.Add(chooseColumnsItem);

        menu.Items.Add(new Separator());
        var hideEmptyColsItem = new MenuItem { Header = "Hide _empty columns" };
        hideEmptyColsItem.Click += (_, _) => HideEmptyColumns(targetGrid, columnRegistry);
        menu.Items.Add(hideEmptyColsItem);

        var showAllColsItem = new MenuItem { Header = "_Show all columns" };
        showAllColsItem.Click += (_, _) => ShowAllColumns(targetGrid, columnRegistry);
        menu.Items.Add(showAllColsItem);

        var showAllExceptEmptyItem = new MenuItem { Header = "Show all _except empty columns" };
        showAllExceptEmptyItem.Click += (_, _) => ShowAllColumnsExceptEmpty(targetGrid, columnRegistry);
        menu.Items.Add(showAllExceptEmptyItem);
        menu.Items.Add(new Separator());

        var hideItem = new MenuItem
        {
            Header = targetColumns.Count > 1 ? "Hide selected columns" : "Hide column"
        };
        hideItem.Click += (_, _) =>
        {
            var visibleCount = targetGrid.Columns.Count;
            var removable = targetColumns
                .Where(c => targetGrid.Columns.Contains(c))
                .ToList();
            if (visibleCount - removable.Count < 1)
            {
                return;
            }

            foreach (var target in removable)
            {
                targetGrid.Columns.Remove(target);
            }

            targetGrid.InvalidateMeasure();
            targetGrid.InvalidateVisual();
        };
        menu.Items.Add(hideItem);
        menu.Items.Add(new Separator());

        var immediateEditItem = new MenuItem
        {
            Header = "Immediate edit",
            IsCheckable = true,
            IsChecked = targetGrid.HostedPlotDirectEditOnMouseDown
        };
        immediateEditItem.Click += (_, _) =>
        {
            targetGrid.HostedPlotDirectEditOnMouseDown = immediateEditItem.IsChecked;
        };
        menu.Items.Add(immediateEditItem);

        var hideSelectionColoringItem = new MenuItem
        {
            Header = "Hide cell selection coloring",
            IsCheckable = true,
            IsChecked = targetGrid.HideCellSelectionColoring
        };
        hideSelectionColoringItem.Click += (_, _) =>
        {
            targetGrid.HideCellSelectionColoring = hideSelectionColoringItem.IsChecked;
            targetGrid.InvalidateVisual();
        };
        menu.Items.Add(hideSelectionColoringItem);

        var hideHeaderSelectionColoringItem = new MenuItem
        {
            Header = "Hide row/col header selection color",
            IsCheckable = true,
            IsChecked = targetGrid.HideHeaderSelectionColoring
        };
        hideHeaderSelectionColoringItem.Click += (_, _) =>
        {
            targetGrid.HideHeaderSelectionColoring = hideHeaderSelectionColoringItem.IsChecked;
            targetGrid.InvalidateVisual();
        };
        menu.Items.Add(hideHeaderSelectionColoringItem);

        var hideCurrentCellColorItem = new MenuItem
        {
            Header = "Hide current cell color",
            IsCheckable = true,
            IsChecked = targetGrid.HideCurrentCellColor
        };
        hideCurrentCellColorItem.Click += (_, _) =>
        {
            targetGrid.HideCurrentCellColor = hideCurrentCellColorItem.IsChecked;
            targetGrid.InvalidateVisual();
        };
        menu.Items.Add(hideCurrentCellColorItem);

        var hideEditCellColorItem = new MenuItem
        {
            Header = "Hide edit cell color",
            IsCheckable = true,
            IsChecked = targetGrid.HideEditCellColor
        };
        hideEditCellColorItem.Click += (_, _) =>
        {
            targetGrid.HideEditCellColor = hideEditCellColorItem.IsChecked;
            targetGrid.InvalidateVisual();
        };
        menu.Items.Add(hideEditCellColorItem);
        menu.Items.Add(new Separator());

        var visibleRowsSubmenu = new MenuItem { Header = "Visible row count" };
        for (var mode = 0; mode <= 10; mode++)
        {
            var localMode = mode;
            var label = localMode == 0 ? "X" : localMode.ToString();
            var modeItem = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = targetGrid.VisibleRowCount == localMode
            };
            modeItem.Click += (_, _) =>
            {
                targetGrid.VisibleRowCount = localMode;
                targetGrid.InvalidateMeasure();
                targetGrid.InvalidateVisual();
            };
            visibleRowsSubmenu.Items.Add(modeItem);
        }
        menu.Items.Add(visibleRowsSubmenu);
        menu.Items.Add(new Separator());

        var columnsSubmenu = new MenuItem { Header = "Columns" };
        foreach (var col in columnRegistry)
        {
            var colMenuItem = new MenuItem
            {
                Header = col.Header,
                IsCheckable = true,
                IsChecked = targetGrid.Columns.Contains(col),
                StaysOpenOnClick = true
            };
            colMenuItem.Click += (_, _) => ToggleColumnVisibility(targetGrid, columnRegistry, col, colMenuItem.IsChecked);
            columnsSubmenu.Items.Add(colMenuItem);
        }
        menu.Items.Add(columnsSubmenu);

        var clickedColumn = targetGrid.Columns[e.ColumnIndex];
        if (string.Equals(clickedColumn.Header, PlottoColumnHeader, StringComparison.Ordinal))
        {
            menu.Items.Add(new Separator());
            var configurePlottoItem = new MenuItem { Header = "Configure Plotto..." };
            configurePlottoItem.Click += (_, _) =>
            {
                var dlg = new PlotConfigurationDialog(ResolvePlotConfigurationChart(targetGrid)) { Owner = this };
                dlg.ShowDialog();
            };
            menu.Items.Add(configurePlottoItem);
        }

        menu.IsOpen = true;
    }

    private ChromatogramControl ResolvePlotConfigurationChart(global::Griddo.Grid.Griddo grid)
    {
        var cell = grid.SelectedCells.FirstOrDefault(c => c.IsValid);
        if (cell.IsValid && grid.TryGetHostedElement(cell) is Border { Child: ChromatogramControl chart })
        {
            return chart;
        }

        return _plotConfigFallback;
    }

    private void ConfigureGrid()
    {
        RegisterColumn(new GriddoColumnView(
            header: "ID",
            width: 70,
            valueGetter: row => ((DemoRow)row).Id,
            valueSetter: (row, value) =>
            {
                var text = value?.ToString();
                if (!int.TryParse(text, out var id))
                {
                    return false;
                }

                ((DemoRow)row).Id = id;
                return true;
            },
            editor: GriddoCellEditors.Number,
            sourceMemberName: nameof(DemoRow.Id)));

        RegisterColumn(new GriddoColumnView(
            header: "Name",
            width: 180,
            valueGetter: row => ((DemoRow)row).Name,
            valueSetter: (row, value) =>
            {
                ((DemoRow)row).Name = value?.ToString() ?? string.Empty;
                return true;
            },
            sourceMemberName: nameof(DemoRow.Name)));

        RegisterColumn(new GriddoColumnView(
            header: "Score",
            width: 100,
            valueGetter: row => ((DemoRow)row).Score,
            valueSetter: (row, value) =>
            {
                var text = value?.ToString();
                if (!double.TryParse(text, out var score))
                {
                    return false;
                }

                ((DemoRow)row).Score = score;
                return true;
            },
            editor: GriddoCellEditors.Number,
            sourceMemberName: nameof(DemoRow.Score)));

        RegisterColumn(new GriddoBoolColumnView(
            header: "Active",
            width: 72,
            valueGetter: row => ((DemoRow)row).Active,
            valueSetter: (row, value) =>
            {
                if (value is not bool b)
                {
                    return false;
                }

                ((DemoRow)row).Active = b;
                return true;
            },
            sourceMemberName: nameof(DemoRow.Active)));

        RegisterColumn(new HtmlGriddoColumnView(
            header: "Html",
            width: 260,
            valueGetter: row => ((DemoRow)row).HtmlSnippet,
            valueSetter: (row, value) =>
            {
                ((DemoRow)row).HtmlSnippet = value;
                return true;
            },
            sourceMemberName: nameof(DemoRow.HtmlSnippet)));

        RegisterColumn(new GriddoColumnView(
            header: "Graphic",
            width: 120,
            valueGetter: row => ((DemoRow)row).Graphic,
            valueSetter: (_, _) => false,
            editor: GriddoCellEditors.Text,
            sourceMemberName: nameof(DemoRow.Graphic)));

        RegisterColumn(new HostedPlottoColumnView(
            header: PlottoColumnHeader,
            width: 220,
            plottoSeedGetter: row => ((DemoRow)row).PlottoSeed));

        RegisterColumn(new HostedCalibrationPlottoColumnView(
            header: CalibrationColumnHeader,
            width: 240,
            seedGetter: row => ((DemoRow)row).PlottoSeed));

        var fillDownItem = new MenuItem
        {
            Header = "Fill _down",
            InputGestureText = "Ctrl+D",
            ToolTip = "Copy the top selected cell in each column to the other selected rows in that column."
        };
        fillDownItem.Click += (_, _) => DemoGrid.FillSelectionDown();

        var incrementalDownItem = new MenuItem
        {
            Header = "_Incremental down",
            InputGestureText = "Ctrl+I",
            ToolTip = "Increment the last integer in each column’s top selected cell for each lower selected row (zero-pad widths)."
        };
        incrementalDownItem.Click += (_, _) => DemoGrid.FillSelectionIncrementalDown();

        DemoGrid.CellContextMenu = new ContextMenu
        {
            Items =
            {
                new MenuItem { Header = "Demo: cell context menu" },
                new Separator(),
                fillDownItem,
                incrementalDownItem,
                new Separator(),
                new MenuItem { Header = "Second item" },
            },
        };

        for (var i = 1; i <= 50_000; i++)
        {
            DemoGrid.Rows.Add(new DemoRow
            {
                Id = i,
                Name = $"Item {i:000}",
                Score = Math.Round(40 + Random.Shared.NextDouble() * 60, 2),
                Active = i % 5 == 0,
                HtmlSnippet = i % 3 == 0
                    ? $"<table><tr><th>R{i}</th><th>Q{i % 4}</th></tr><tr><td>{i * 2}</td><td><b>{Math.Round(i / 3.0, 2)}</b></td></tr></table>"
                    : $"<b>Row {i}</b> has <i>formatted</i> text",
                Graphic = CreatePathMarkupDemo(i),
                PlottoSeed = i
            });
        }
    }

    private void RegisterColumn(IGriddoColumnView column)
    {
        _allColumns.Add(column);
        DemoGrid.Columns.Add(column);
    }

    /// <summary>True when every row’s formatted value for this column is null/whitespace (no rows ⇒ not empty).</summary>
    private static bool IsColumnEmptyForAllRows(global::Griddo.Grid.Griddo grid, IGriddoColumnView column)
    {
        if (grid.Rows.Count == 0)
        {
            return false;
        }

        foreach (var row in grid.Rows)
        {
            var formatted = column.FormatValue(column.GetValue(row));
            if (!string.IsNullOrWhiteSpace(formatted))
            {
                return false;
            }
        }

        return true;
    }

    private static void HideEmptyColumns(global::Griddo.Grid.Griddo grid, IReadOnlyList<IGriddoColumnView> registry)
    {
        var toHide = new List<IGriddoColumnView>();
        foreach (var col in registry)
        {
            if (!grid.Columns.Contains(col))
            {
                continue;
            }

            if (IsColumnEmptyForAllRows(grid, col))
            {
                toHide.Add(col);
            }
        }

        foreach (var col in toHide)
        {
            if (grid.Columns.Count <= 1)
            {
                break;
            }

            if (grid.Columns.Contains(col))
            {
                grid.Columns.Remove(col);
            }
        }

        grid.InvalidateMeasure();
        grid.InvalidateVisual();
    }

    private static void ShowAllColumns(global::Griddo.Grid.Griddo grid, IReadOnlyList<IGriddoColumnView> registry)
    {
        foreach (var col in registry)
        {
            ToggleColumnVisibility(grid, registry, col, true);
        }
    }

    private static void ShowAllColumnsExceptEmpty(global::Griddo.Grid.Griddo grid, IReadOnlyList<IGriddoColumnView> registry)
    {
        ShowAllColumns(grid, registry);
        HideEmptyColumns(grid, registry);
    }

    private static void ToggleColumnVisibility(
        global::Griddo.Grid.Griddo grid,
        IReadOnlyList<IGriddoColumnView> registry,
        IGriddoColumnView column,
        bool? shouldShow)
    {
        var isVisible = grid.Columns.Contains(column);
        if (shouldShow == isVisible)
        {
            return;
        }

        if (shouldShow != true)
        {
            if (grid.Columns.Count <= 1)
            {
                return;
            }

            grid.Columns.Remove(column);
        }
        else
        {
            var insertIndex = 0;
            foreach (var orderedColumn in registry)
            {
                if (ReferenceEquals(orderedColumn, column))
                {
                    break;
                }

                if (grid.Columns.Contains(orderedColumn))
                {
                    insertIndex++;
                }
            }

            insertIndex = Math.Clamp(insertIndex, 0, grid.Columns.Count);
            grid.Columns.Insert(insertIndex, column);
        }

        grid.InvalidateMeasure();
        grid.InvalidateVisual();
    }

    private static List<IGriddoColumnView> GetSelectedVisibleColumns(global::Griddo.Grid.Griddo grid)
    {
        var selectedIndices = grid.SelectedCells
            .Select(c => c.ColumnIndex)
            .Where(index => index >= 0 && index < grid.Columns.Count)
            .Distinct()
            .OrderBy(index => index)
            .ToList();

        var selectedColumns = new List<IGriddoColumnView>(selectedIndices.Count);
        foreach (var index in selectedIndices)
        {
            selectedColumns.Add(grid.Columns[index]);
        }

        return selectedColumns;
    }

    /// <summary>Five calibration standards; Y values vary slightly by row seed. Fit mode cycles Linear / LinearThroughOrigin / Quadratic / QuadraticThroughOrigin by seed.</summary>
    internal static List<CalibrationPoint> CreateCalibrationPoints(int seed)
    {
        var r = new Random(seed);
        var xs = new[] { 0.5, 1.0, 2.0, 4.0, 8.0 };
        var list = new List<CalibrationPoint>(5);
        foreach (var x in xs)
        {
            var yIdeal = 0.15 + 0.55 * x + 0.035 * x * x;
            var noise = (r.NextDouble() - 0.5) * 1.1;
            list.Add(new CalibrationPoint
            {
                X = x,
                Y = Math.Round(yIdeal + noise, 2),
                IsEnabled = true
            });
        }

        return list;
    }

    internal static IReadOnlyList<ChartPoint> CreateChromatogramPoints(int seed)
    {
        const int count = 64;
        var points = new ChartPoint[count];
        var r = new Random(seed);
        var peakCenter = 0.2 + r.NextDouble() * 0.6;
        var peakWidth = 0.03 + r.NextDouble() * 0.07;
        var shoulderCenter = Math.Min(0.95, peakCenter + 0.12 + r.NextDouble() * 0.08);
        var shoulderWidth = peakWidth * 1.5;

        for (var i = 0; i < count; i++)
        {
            var x = i / (double)(count - 1);
            var y = 2
                    + 90 * Gaussian(x, peakCenter, peakWidth)
                    + 35 * Gaussian(x, shoulderCenter, shoulderWidth)
                    + r.NextDouble() * 1.5;
            points[i] = new ChartPoint(x, y);
        }

        return points;
    }

    private static double Gaussian(double x, double mean, double sigma)
    {
        var z = (x - mean) / sigma;
        return Math.Exp(-0.5 * z * z);
    }

    /// <summary>
    /// Row-dependent mini drawings (~60×56 logical coords); scaled into the cell by Griddo.
    /// Cycles <see cref="PathMarkupVariants"/> so adjacent rows usually differ.
    /// </summary>
    private static Geometry CreatePathMarkupDemo(int seed)
    {
        var pathData = PathMarkupVariants[seed % PathMarkupVariants.Length];
        var g = Geometry.Parse(pathData);
        g.Freeze();
        return g;
    }

    private void ChooseColumns_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        OpenChooseColumnsDialog(DemoGrid);
    }

    private void OpenChooseColumnsDialog(global::Griddo.Grid.Griddo grid)
    {
        var rows = ColumnMetadataBuilder.BuildRowsFromGrid(grid, _allColumns);
        var dlg = new ColumnEditDialog(rows, grid.FixedColumnCount, grid.FixedRowCount) { Owner = this };
        dlg.TargetSourceGrid = grid;
        dlg.ApplyToSourceGrid = (r, fc, fr) => ColumnChooserGridApplier.Apply(grid, r, fc, fr, _allColumns);
        dlg.ColumnHeaderMenuHandler = (g, ev) => OnColumnHeaderRightClick(g, dlg.ColumnHeaderRegistry, grid, ev);
        grid.ClearCellSelection();
        dlg.ShowDialog();
    }

    /// <summary>WPF path markup samples—triangle, circle, diamond, rect, hexagon, heart-ish, wave, etc.</summary>
    private static readonly string[] PathMarkupVariants =
    [
        // Triangle
        "M 30,10 L 48,46 L 12,46 Z",
        // Circle (two arcs)
        "M 50,28 A 20,20 0 1 1 10,28 A 20,20 0 1 1 50,28",
        // Diamond
        "M 30,8 L 50,30 L 30,52 L 10,30 Z",
        // Rounded square (Q corners)
        "M 18,16 Q 14,16 14,20 V 40 Q 14,44 18,44 H 42 Q 46,44 46,40 V 20 Q 46,16 42,16 Z",
        // House (square + roof)
        "M 16,40 V 22 H 44 V 40 Z M 30,10 L 44,22 H 16 Z",
        // Hexagon
        "M 30,8 L 49,19 L 49,41 L 30,52 L 11,41 L 11,19 Z",
        // Heart (two cubics)
        "M 30,46 C 14,34 8,22 30,14 C 52,22 46,34 30,46 Z",
        // S-curve ribbon (C/S)
        "M 8,40 C 8,14 52,46 52,20 L 52,46 H 8 Z",
        // Arrow (filled chevron)
        "M 10,30 L 36,12 L 36,22 H 50 V 38 H 36 L 36,48 Z",
        // Ellipse (two arcs)
        "M 54,30 A 22,12 0 1 1 6,30 A 22,12 0 1 1 54,30",
        // Plus (fat cross)
        "M 22,14 H 38 V 22 H 46 V 38 H 38 V 46 H 22 V 38 H 14 V 22 H 22 Z",
        // Star (5-point)
        "M 30,10 L 36,26 L 52,26 L 40,36 L 46,52 L 30,42 L 14,52 L 20,36 L 8,26 L 24,26 Z"
    ];
}

public sealed class DemoRow
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Score { get; set; }
    public bool Active { get; set; }
    public string HtmlSnippet { get; set; } = string.Empty;
    public Geometry Graphic { get; set; } = Geometry.Empty;
    public int PlottoSeed { get; set; }
}
