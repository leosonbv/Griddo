using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using System.Net;
using Griddo;
using Griddo.Fields;
using Griddo.Editing;
using Griddo.Grid;
using Plotto.Charting.Controls;
using Plotto.Charting.Core;
using GriddoUi.FieldEdit;
using GriddoModelView;

namespace GriddoTest;

public partial class MainWindow : Window
{
    private const string ChromatogramFieldHeader = "Chromatogram";
    private const string CalibrationFieldHeader = "Calibration";
    private const string DemoGridLayoutKey = "GriddoTest.MainWindow.DemoGrid";
    private const string ConfigFieldsGridLayoutKey = "GriddoTest.MainWindow.Config.Fields";
    private const string ConfigGeneralGridLayoutKey = "GriddoTest.MainWindow.Config.General";
    private const string PrimarySource = "Primary";
    private const string AnalyticsSource = "Analytics";
    private readonly List<IGriddoFieldView> _allFields = [];
    private ComposedHtmlFieldView? _htmlField;
    private readonly PropertyViewStore _viewStore = new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Griddo",
            "source-property-views.json"));
    private readonly GridConfigurationStore _gridLayoutStore = new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Griddo",
            "grid-layouts.json"));

    private readonly ChromatogramControl _plotConfigFallback = new()
    {
        AxisLabelX = "Time",
        AxisLabelY = "Intensity",
        ChartTitle = "Chromatogram"
    };

    public MainWindow()
    {
        InitializeComponent();
        _viewStore.Load();
        _gridLayoutStore.Load();
        ConfigureGrid();
        DemoGrid.FieldHeaderRightClick += (_, e) => OnFieldHeaderRightClick(DemoGrid, _allFields, e);
        DemoGrid.RecordHeaderRightClick += (_, e) => OnRecordHeaderRightClick(DemoGrid, _allFields, e);
        DemoGrid.CornerHeaderRightClick += (_, _) => OnCornerHeaderRightClick(DemoGrid, _allFields);
        DemoGrid.SortDescriptorsChanged += (_, _) => PersistGridLayoutFromCurrentGrid(DemoGrid, DemoGridLayoutKey, _allFields);
        DemoGrid.UniformRecordHeightChanged += (_, _) => PersistGridLayoutFromCurrentGrid(DemoGrid, DemoGridLayoutKey, _allFields);
        DemoGrid.UniformRecordHeight = 132;
        DemoGrid.FixedFieldCount = 1;
        DemoGrid.ImmediateCellEditOnSingleClick = false;
        DemoGrid.HostedPlotDirectEditOnMouseDown = true;
        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(
                () =>
                {
                    // Let queued startup layout/reformat work complete before first paint.
                    DemoGrid.UpdateLayout();
                    DemoGrid.InvalidateMeasure();
                    DemoGrid.InvalidateVisual();
                    Dispatcher.BeginInvoke(
                        () => DemoGrid.Visibility = Visibility.Visible,
                        System.Windows.Threading.DispatcherPriority.ContextIdle);
                },
                System.Windows.Threading.DispatcherPriority.Loaded);
        };
    }

    private void OnFieldHeaderRightClick(
        global::Griddo.Grid.Griddo targetGrid,
        IReadOnlyList<IGriddoFieldView> fieldRegistry,
        GriddoFieldHeaderMouseEventArgs e)
    {
        if (e.FieldIndex < 0 || e.FieldIndex >= targetGrid.Fields.Count)
        {
            return;
        }

        var indices = e.SelectedFieldIndices
            .Where(i => i >= 0 && i < targetGrid.Fields.Count)
            .Distinct()
            .OrderBy(i => i)
            .ToList();
        if (indices.Count == 0)
        {
            return;
        }

        var targetFields = indices.Select(i => targetGrid.Fields[i]).ToList();
        var selectedPlotTargets = targetFields.OfType<IPlotFieldLayoutTarget>().ToList();
        var selectedHtmlTargets = targetFields.OfType<IHtmlFieldLayoutTarget>().ToList();
        var menu = new ContextMenu
        {
            PlacementTarget = targetGrid,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
        };
        var layoutKey = ResolveLayoutKey(targetGrid, fieldRegistry);
        void PersistLiveLayout() => PersistGridLayoutFromCurrentGrid(targetGrid, layoutKey, fieldRegistry);

        var gridConfiguratorItem = new MenuItem { Header = "_Grid configurator…" };
        gridConfiguratorItem.Click += (_, _) => OpenFieldConfigurator(targetGrid, fieldRegistry, layoutKey);
        menu.Items.Add(gridConfiguratorItem);
        var frozenFieldsItem = new MenuItem
        {
            Header = "_Frozen",
            IsCheckable = true,
            IsChecked = indices.All(i => i < targetGrid.FixedFieldCount)
        };
        frozenFieldsItem.Click += (_, _) =>
        {
            ApplyFieldFrozenSelection(targetGrid, indices, frozenFieldsItem.IsChecked);
            PersistLiveLayout();
        };
        menu.Items.Add(frozenFieldsItem);
        var appearanceSubmenu = new MenuItem { Header = "Grid features", StaysOpenOnClick = true };
        menu.Items.Add(appearanceSubmenu);
        menu.Items.Add(new Separator());
        var autoWidthAllFieldsItem = new MenuItem { Header = "Auto size all fields" };
        autoWidthAllFieldsItem.Click += (_, _) => targetGrid.AutoSizeAllFields();
        menu.Items.Add(autoWidthAllFieldsItem);
        var autoWidthSelectedFieldsItem = new MenuItem { Header = "Auto size selected field(s)" };
        autoWidthSelectedFieldsItem.Click += (_, _) => targetGrid.AutoSizeFields(indices);
        menu.Items.Add(autoWidthSelectedFieldsItem);
        var fillItem = new MenuItem
        {
            Header = "Fill size for selected field(s)",
            IsCheckable = true,
            IsChecked = targetFields.All(c => c.Fill)
        };
        fillItem.Click += (_, _) =>
        {
            foreach (var target in targetFields)
            {
                target.Fill = fillItem.IsChecked;
            }

            targetGrid.InvalidateMeasure();
            targetGrid.InvalidateVisual();
            PersistLiveLayout();
        };
        menu.Items.Add(fillItem);
        if (selectedPlotTargets.Count > 0)
        {
            var plotSettingsItem = new MenuItem { Header = "Plot field settings..." };
            plotSettingsItem.Click += (_, _) =>
            {
                var seed = selectedPlotTargets[0];
                var dialog = new PlotConfigurationDialog(
                    seed,
                    previewApply: result =>
                    {
                        foreach (var target in selectedPlotTargets)
                        {
                            ApplyPlotLayout(target, result);
                        }

                        targetGrid.RefreshHostedCells();
                        targetGrid.InvalidateVisual();
                    })
                { Owner = this };
                if (dialog.ShowDialog() != true || dialog.Result is null)
                {
                    return;
                }

                foreach (var target in selectedPlotTargets)
                {
                    ApplyPlotLayout(target, dialog.Result);
                }

                targetGrid.RefreshHostedCells();
                targetGrid.InvalidateVisual();
                PersistLiveLayout();
            };
            menu.Items.Add(plotSettingsItem);
        }
        if (selectedHtmlTargets.Count > 0)
        {
            var htmlSettingsItem = new MenuItem { Header = "HTML field settings..." };
            htmlSettingsItem.Click += (_, _) =>
            {
                var seed = selectedHtmlTargets[0];
                var dialog = new HtmlConfigurationDialog(
                    seed,
                    _allFields,
                    previewApply: result =>
                    {
                        foreach (var target in selectedHtmlTargets)
                        {
                            ApplyHtmlLayout(target, result);
                        }

                        targetGrid.InvalidateVisual();
                        PersistLiveLayout();
                    })
                { Owner = this };
                if (dialog.ShowDialog() != true || dialog.Result is null)
                {
                    return;
                }

                foreach (var target in selectedHtmlTargets)
                {
                    ApplyHtmlLayout(target, dialog.Result);
                }

                targetGrid.InvalidateVisual();
                PersistLiveLayout();
            };
            menu.Items.Add(htmlSettingsItem);
        }
        menu.Items.Add(new Separator());

        var visibilitySubmenu = new MenuItem { Header = "Field visibility actions" };
        var hideSelectedFieldsItem = new MenuItem { Header = "Hide selected field(s)" };
        hideSelectedFieldsItem.Click += (_, _) =>
        {
            var visibleCount = targetGrid.Fields.Count;
            var removable = targetFields.Where(c => targetGrid.Fields.Contains(c)).ToList();
            if (visibleCount - removable.Count < 1)
            {
                return;
            }

            foreach (var target in removable)
            {
                targetGrid.Fields.Remove(target);
            }

            targetGrid.InvalidateMeasure();
            targetGrid.InvalidateVisual();
            PersistLiveLayout();
        };
        visibilitySubmenu.Items.Add(hideSelectedFieldsItem);
        visibilitySubmenu.Items.Add(new Separator());

        var hideEmptyColsItem = new MenuItem { Header = "Hide _empty fields" };
        hideEmptyColsItem.Click += (_, _) =>
        {
            HideEmptyFields(targetGrid, fieldRegistry);
            PersistLiveLayout();
        };
        visibilitySubmenu.Items.Add(hideEmptyColsItem);

        var showAllColsItem = new MenuItem { Header = "_Show all fields" };
        showAllColsItem.Click += (_, _) =>
        {
            ShowAllFields(targetGrid, fieldRegistry);
            PersistLiveLayout();
        };
        visibilitySubmenu.Items.Add(showAllColsItem);

        var showAllExceptEmptyItem = new MenuItem { Header = "Show all _except empty fields" };
        showAllExceptEmptyItem.Click += (_, _) =>
        {
            ShowAllFieldsExceptEmpty(targetGrid, fieldRegistry);
            PersistLiveLayout();
        };
        visibilitySubmenu.Items.Add(showAllExceptEmptyItem);
        visibilitySubmenu.Items.Add(new Separator());

        var visibilityListItem = new MenuItem { Header = "Fields" };
        foreach (var registeredField in fieldRegistry)
        {
            var localField = registeredField;
            var listItem = new MenuItem
            {
                Header = GetFieldVisibilityLabel(localField),
                IsCheckable = true,
                IsChecked = targetGrid.Fields.Contains(localField),
                StaysOpenOnClick = true
            };
            listItem.Click += (_, _) =>
            {
                ToggleFieldVisibility(targetGrid, fieldRegistry, localField, listItem.IsChecked);
                PersistLiveLayout();
            };
            visibilityListItem.Items.Add(listItem);
        }

        visibilitySubmenu.Items.Add(visibilityListItem);
        menu.Items.Add(visibilitySubmenu);
        menu.Items.Add(new Separator());

        var sortSubmenu = new MenuItem { Header = "Sort" };
        void ApplySortForSelectedFields(bool ascending)
        {
            var selectedFieldIndices = indices
                .Where(i => i >= 0 && i < targetGrid.Fields.Count)
                .Distinct()
                .OrderBy(i => i)
                .ToList();
            if (selectedFieldIndices.Count == 0)
            {
                return;
            }

            var isCtrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (!isCtrlPressed)
            {
                var descriptors = selectedFieldIndices
                    .Select((fieldIndex, level) => new GriddoSortDescriptor(fieldIndex, ascending, level + 1))
                    .ToList();
                targetGrid.SetSortDescriptors(descriptors);
                PersistLiveLayout();
                return;
            }

            var existing = targetGrid.SortDescriptors
                .OrderBy(d => d.Priority)
                .ToList();
            var existingFields = existing
                .Select(d => d.FieldIndex)
                .ToHashSet();
            var nextPriority = existing.Count == 0 ? 1 : existing.Max(d => d.Priority) + 1;
            foreach (var fieldIndex in selectedFieldIndices)
            {
                if (!existingFields.Add(fieldIndex))
                {
                    continue;
                }

                existing.Add(new GriddoSortDescriptor(fieldIndex, ascending, nextPriority));
                nextPriority++;
            }

            targetGrid.SetSortDescriptors(existing);
            PersistLiveLayout();
        }

        var sortAscItem = new MenuItem { Header = "Ascending" };
        sortAscItem.Click += (_, _) =>
        {
            ApplySortForSelectedFields(ascending: true);
        };
        sortSubmenu.Items.Add(sortAscItem);

        var sortDescItem = new MenuItem { Header = "Descending" };
        sortDescItem.Click += (_, _) =>
        {
            ApplySortForSelectedFields(ascending: false);
        };
        sortSubmenu.Items.Add(sortDescItem);

        var sortClearItem = new MenuItem { Header = "Clear sort" };
        sortClearItem.Click += (_, _) =>
        {
            targetGrid.SetSortDescriptors([]);
            PersistLiveLayout();
        };
        sortSubmenu.Items.Add(sortClearItem);
        var sortRemoveSelectedItem = new MenuItem { Header = "Remove sort (selected fields)" };
        sortRemoveSelectedItem.Click += (_, _) =>
        {
            var selectedFieldIndices = indices
                .Where(i => i >= 0 && i < targetGrid.Fields.Count)
                .Distinct()
                .ToHashSet();
            if (selectedFieldIndices.Count == 0)
            {
                return;
            }

            var keptDescriptors = targetGrid.SortDescriptors
                .OrderBy(d => d.Priority)
                .Where(d => !selectedFieldIndices.Contains(d.FieldIndex))
                .ToList();
            targetGrid.SetSortDescriptors(keptDescriptors);
            PersistLiveLayout();
        };
        sortSubmenu.Items.Add(sortRemoveSelectedItem);
        sortSubmenu.Items.Add(new Separator());
        sortSubmenu.Items.Add(new MenuItem
        {
            Header = "Tip: Ctrl+click header adds sort level",
            IsEnabled = false
        });
        menu.Items.Add(sortSubmenu);

        var immediateEditItem = new MenuItem
        {
            Header = "Immediate plot edit",
            IsCheckable = true,
            IsChecked = targetGrid.HostedPlotDirectEditOnMouseDown,
            StaysOpenOnClick = true
        };
        immediateEditItem.Click += (_, _) =>
        {
            targetGrid.HostedPlotDirectEditOnMouseDown = immediateEditItem.IsChecked;
            PersistLiveLayout();
        };
        appearanceSubmenu.Items.Add(immediateEditItem);

        var showSelectionColoringItem = new MenuItem
        {
            Header = "Show cell selection color",
            IsCheckable = true,
            IsChecked = targetGrid.ShowCellSelectionColoring,
            StaysOpenOnClick = true
        };
        showSelectionColoringItem.Click += (_, _) =>
        {
            targetGrid.ShowCellSelectionColoring = showSelectionColoringItem.IsChecked;
            targetGrid.InvalidateVisual();
            PersistLiveLayout();
        };
        appearanceSubmenu.Items.Add(showSelectionColoringItem);

        var showHeaderSelectionColoringItem = new MenuItem
        {
            Header = "Show header selection color",
            IsCheckable = true,
            IsChecked = targetGrid.ShowHeaderSelectionColoring,
            StaysOpenOnClick = true
        };
        showHeaderSelectionColoringItem.Click += (_, _) =>
        {
            targetGrid.ShowHeaderSelectionColoring = showHeaderSelectionColoringItem.IsChecked;
            targetGrid.InvalidateVisual();
            PersistLiveLayout();
        };
        appearanceSubmenu.Items.Add(showHeaderSelectionColoringItem);

        var showCurrentCellColorItem = new MenuItem
        {
            Header = "Show current cell color",
            IsCheckable = true,
            IsChecked = targetGrid.ShowCurrentCellColor,
            StaysOpenOnClick = true
        };
        showCurrentCellColorItem.Click += (_, _) =>
        {
            targetGrid.ShowCurrentCellColor = showCurrentCellColorItem.IsChecked;
            targetGrid.InvalidateVisual();
            PersistLiveLayout();
        };
        appearanceSubmenu.Items.Add(showCurrentCellColorItem);

        var showEditCellColorItem = new MenuItem
        {
            Header = "Show edit cell color",
            IsCheckable = true,
            IsChecked = targetGrid.ShowEditCellColor,
            StaysOpenOnClick = true
        };
        showEditCellColorItem.Click += (_, _) =>
        {
            targetGrid.ShowEditCellColor = showEditCellColorItem.IsChecked;
            targetGrid.InvalidateVisual();
            PersistLiveLayout();
        };
        appearanceSubmenu.Items.Add(showEditCellColorItem);

        var showSortIndicatorsItem = new MenuItem
        {
            Header = "Show sort indicators",
            IsCheckable = true,
            IsChecked = targetGrid.ShowSortingIndicators,
            StaysOpenOnClick = true
        };
        showSortIndicatorsItem.Click += (_, _) =>
        {
            targetGrid.ShowSortingIndicators = showSortIndicatorsItem.IsChecked;
            targetGrid.InvalidateVisual();
            PersistLiveLayout();
        };
        appearanceSubmenu.Items.Add(showSortIndicatorsItem);

        var visibleRecordsSubmenu = new MenuItem { Header = "Fill records" };
        for (var mode = 0; mode <= 10; mode++)
        {
            var localMode = mode;
            var label = localMode == 0 ? "Auto" : localMode.ToString();
            var modeItem = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = targetGrid.VisibleRecordCount == localMode
            };
            modeItem.Click += (_, _) =>
            {
                targetGrid.VisibleRecordCount = localMode;
                targetGrid.InvalidateMeasure();
                targetGrid.InvalidateVisual();
                PersistLiveLayout();
            };
            visibleRecordsSubmenu.Items.Add(modeItem);
        }
        menu.Items.Add(visibleRecordsSubmenu);
        menu.Items.Add(new Separator());

        var findSubmenu = new MenuItem { Header = "Find" };
        var findItem = new MenuItem { Header = "Find…", InputGestureText = "Ctrl+F" };
        findItem.Click += (_, _) => targetGrid.OpenFindDialogAndFindFirst();
        findSubmenu.Items.Add(findItem);

        var findNextItem = new MenuItem { Header = "Find next", InputGestureText = "F3" };
        findNextItem.Click += (_, _) => targetGrid.FindNext();
        findSubmenu.Items.Add(findNextItem);

        var findPreviousItem = new MenuItem { Header = "Find previous", InputGestureText = "Shift+F3" };
        findPreviousItem.Click += (_, _) => targetGrid.FindPrevious();
        findSubmenu.Items.Add(findPreviousItem);

        var findCancelItem = new MenuItem { Header = "Cancel", InputGestureText = "Esc" };
        findCancelItem.Click += (_, _) => targetGrid.CancelFind();
        findSubmenu.Items.Add(findCancelItem);
        menu.Items.Add(findSubmenu);

        menu.IsOpen = true;
    }

    private void OnRecordHeaderRightClick(
        global::Griddo.Grid.Griddo targetGrid,
        IReadOnlyList<IGriddoFieldView> fieldRegistry,
        GriddoRecordHeaderMouseEventArgs e)
    {
        if (e.RecordIndex < 0 || e.RecordIndex >= targetGrid.Records.Count)
        {
            return;
        }

        var indices = e.SelectedRecordIndices
            .Where(i => i >= 0 && i < targetGrid.Records.Count)
            .Distinct()
            .OrderBy(i => i)
            .ToList();
        if (indices.Count == 0)
        {
            return;
        }

        var layoutKey = ResolveLayoutKey(targetGrid, fieldRegistry);
        var menu = new ContextMenu
        {
            PlacementTarget = targetGrid,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
        };
        var gridConfiguratorItem = new MenuItem { Header = "_Grid configurator…" };
        gridConfiguratorItem.Click += (_, _) => OpenFieldConfigurator(targetGrid, fieldRegistry, layoutKey);
        menu.Items.Add(gridConfiguratorItem);
        var frozenRecordsItem = new MenuItem
        {
            Header = "_Frozen",
            IsCheckable = true,
            IsChecked = indices.All(i => i < targetGrid.FixedRecordCount)
        };
        frozenRecordsItem.Click += (_, _) =>
        {
            ApplyRecordFrozenSelection(targetGrid, indices, frozenRecordsItem.IsChecked);
            PersistGridLayoutFromCurrentGrid(targetGrid, layoutKey, fieldRegistry);
        };
        menu.Items.Add(frozenRecordsItem);
        menu.IsOpen = true;
    }

    private void OnCornerHeaderRightClick(
        global::Griddo.Grid.Griddo targetGrid,
        IReadOnlyList<IGriddoFieldView> fieldRegistry)
    {
        OpenGridConfiguratorContextMenu(targetGrid, fieldRegistry);
    }

    private void OpenGridConfiguratorContextMenu(
        global::Griddo.Grid.Griddo targetGrid,
        IReadOnlyList<IGriddoFieldView> fieldRegistry)
    {
        var layoutKey = ResolveLayoutKey(targetGrid, fieldRegistry);
        var menu = new ContextMenu
        {
            PlacementTarget = targetGrid,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
        };
        var gridConfiguratorItem = new MenuItem { Header = "_Grid configurator…" };
        gridConfiguratorItem.Click += (_, _) => OpenFieldConfigurator(targetGrid, fieldRegistry, layoutKey);
        menu.Items.Add(gridConfiguratorItem);
        menu.IsOpen = true;
    }

    private static void ApplyFieldFrozenSelection(global::Griddo.Grid.Griddo grid, IReadOnlyList<int> selectedIndices, bool shouldFreeze)
    {
        if (selectedIndices.Count == 0 || grid.Fields.Count == 0)
        {
            return;
        }

        var selected = selectedIndices
            .Where(i => i >= 0 && i < grid.Fields.Count)
            .Distinct()
            .OrderBy(i => i)
            .Select(i => grid.Fields[i])
            .Distinct()
            .ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var frozenCount = Math.Clamp(grid.FixedFieldCount, 0, grid.Fields.Count);
        if (shouldFreeze)
        {
            foreach (var field in selected)
            {
                var currentIndex = grid.Fields.IndexOf(field);
                if (currentIndex < 0)
                {
                    continue;
                }

                if (currentIndex < frozenCount)
                {
                    // Already frozen: move to end of frozen block.
                    grid.Fields.Move(currentIndex, Math.Max(0, frozenCount - 1));
                    continue;
                }

                // Not frozen yet: insert at end of frozen block, then extend block size.
                grid.Fields.Move(currentIndex, frozenCount);
                frozenCount++;
            }
        }
        else
        {
            foreach (var field in selected)
            {
                var currentIndex = grid.Fields.IndexOf(field);
                if (currentIndex < 0 || currentIndex >= frozenCount)
                {
                    continue;
                }

                // Remove from frozen block and place at start of non-frozen block.
                frozenCount--;
                grid.Fields.Move(currentIndex, frozenCount);
            }
        }

        RestoreFieldSelection(grid, selected);
        grid.FixedFieldCount = Math.Clamp(frozenCount, 0, grid.Fields.Count);
        grid.InvalidateMeasure();
        grid.InvalidateVisual();
    }

    private static void RestoreFieldSelection(global::Griddo.Grid.Griddo grid, IReadOnlyList<IGriddoFieldView> selectedFields)
    {
        if (selectedFields.Count == 0 || grid.Fields.Count == 0 || grid.Records.Count == 0)
        {
            return;
        }

        var selectedIndices = selectedFields
            .Select(grid.Fields.IndexOf)
            .Where(i => i >= 0)
            .Distinct()
            .OrderBy(i => i)
            .ToList();
        if (selectedIndices.Count == 0)
        {
            return;
        }

        grid.ClearCellSelection();
        for (var i = 0; i < selectedIndices.Count; i++)
        {
            grid.SelectEntireField(selectedIndices[i], additive: i > 0);
        }
    }

    private static void ApplyRecordFrozenSelection(global::Griddo.Grid.Griddo grid, IReadOnlyList<int> selectedIndices, bool shouldFreeze)
    {
        if (selectedIndices.Count == 0 || grid.Records.Count == 0)
        {
            return;
        }

        var selected = selectedIndices
            .Where(i => i >= 0 && i < grid.Records.Count)
            .Distinct()
            .OrderBy(i => i)
            .Select(i => grid.Records[i])
            .Distinct()
            .ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var frozenCount = Math.Clamp(grid.FixedRecordCount, 0, grid.Records.Count);
        if (shouldFreeze)
        {
            foreach (var record in selected)
            {
                var currentIndex = grid.Records.IndexOf(record);
                if (currentIndex < 0)
                {
                    continue;
                }

                if (currentIndex < frozenCount)
                {
                    // Already frozen: move to the end of the frozen block.
                    grid.Records.Move(currentIndex, Math.Max(0, frozenCount - 1));
                    continue;
                }

                // Not frozen yet: insert at end of frozen block, then extend block size.
                grid.Records.Move(currentIndex, frozenCount);
                frozenCount++;
            }
        }
        else
        {
            foreach (var record in selected)
            {
                var currentIndex = grid.Records.IndexOf(record);
                if (currentIndex < 0 || currentIndex >= frozenCount)
                {
                    continue;
                }

                // Remove from frozen block and place at start of non-frozen block.
                frozenCount--;
                grid.Records.Move(currentIndex, frozenCount);
            }
        }

        RestoreRecordSelection(grid, selected);
        grid.FixedRecordCount = Math.Clamp(frozenCount, 0, grid.Records.Count);
        grid.InvalidateMeasure();
        grid.InvalidateVisual();
    }

    private static void RestoreRecordSelection(global::Griddo.Grid.Griddo grid, IReadOnlyList<object> selectedRecords)
    {
        if (selectedRecords.Count == 0 || grid.Fields.Count == 0 || grid.Records.Count == 0)
        {
            return;
        }

        var selectedIndices = selectedRecords
            .Select(grid.Records.IndexOf)
            .Where(i => i >= 0)
            .Distinct()
            .OrderBy(i => i)
            .ToList();
        if (selectedIndices.Count == 0)
        {
            return;
        }

        grid.ClearCellSelection();
        for (var i = 0; i < selectedIndices.Count; i++)
        {
            grid.SelectEntireRecord(selectedIndices[i], additive: i > 0);
        }
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
        RegisterField(GriddoNamedSourceFields.Create(
            sourceObjectName: PrimarySource,
            sourceMemberName: nameof(PrimaryDemoSource.Id),
            header: "ID",
            width: 70,
            editor: GriddoCellEditors.Number));

        RegisterField(GriddoNamedSourceFields.Create(
            sourceObjectName: PrimarySource,
            sourceMemberName: nameof(PrimaryDemoSource.Name),
            header: "Name",
            width: 180));

        RegisterField(new HierarchicalMergeTextFieldView(
            header: "Result flag",
            width: 120,
            sourceObjectName: PrimarySource,
            sourceMemberName: nameof(PrimaryDemoSource.ResultFlagKey),
            recordsAccessor: () => DemoGrid.Records,
            displayGetter: record => GetPrimarySource(record).ResultFlagDisplay,
            sortKeyGetter: record => GetPrimarySource(record).ResultFlagKey,
            mergeKeyGetters:
            [
                record => GetPrimarySource(record).ResultFlagKey
            ]));

        RegisterField(new HierarchicalMergeTextFieldView(
            header: "Review status",
            width: 130,
            sourceObjectName: PrimarySource,
            sourceMemberName: nameof(PrimaryDemoSource.ReviewStatusKey),
            recordsAccessor: () => DemoGrid.Records,
            displayGetter: record => GetPrimarySource(record).ReviewStatusDisplay,
            sortKeyGetter: record => GetPrimarySource(record).ReviewStatusKey,
            mergeKeyGetters:
            [
                record => GetPrimarySource(record).ResultFlagKey,
                record => GetPrimarySource(record).ReviewStatusKey
            ]));

        RegisterField(GriddoNamedSourceFields.Create(
            sourceObjectName: AnalyticsSource,
            sourceMemberName: nameof(AnalyticsDemoSource.Score),
            header: "Score",
            width: 100,
            editor: GriddoCellEditors.Number));

        RegisterField(new GriddoBoolFieldView(
            header: "Active",
            width: 72,
            valueGetter: record => GetPrimarySource(record).Active,
            valueSetter: (record, value) =>
            {
                if (value is not bool b)
                {
                    return false;
                }

                GetPrimarySource(record).Active = b;
                return true;
            },
            sourceMemberName: nameof(PrimaryDemoSource.Active),
            sourceObjectName: PrimarySource));

        _htmlField = new ComposedHtmlFieldView(
            header: "Html",
            width: 260,
            sourceObjectName: PrimarySource,
            sourceMemberName: nameof(PrimaryDemoSource.HtmlSnippet),
            allFieldsAccessor: () => _allFields);
        RegisterField(_htmlField);

        RegisterField(new GriddoFieldView(
            header: "Graphic",
            width: 120,
            valueGetter: record => GetPrimarySource(record).Graphic,
            valueSetter: (_, _) => false,
            editor: GriddoCellEditors.Text,
            sourceMemberName: nameof(PrimaryDemoSource.Graphic),
            sourceObjectName: PrimarySource));

        RegisterField(new HostedPlottoFieldView(
            header: ChromatogramFieldHeader,
            width: 220,
            plottoSeedGetter: record => GetAnalyticsSource(record).PlottoSeed,
            sourceObjectName: AnalyticsSource,
            sourceMemberName: nameof(AnalyticsDemoSource.PlottoSeed)));

        RegisterField(new HostedCalibrationPlottoFieldView(
            header: CalibrationFieldHeader,
            width: 240,
            seedGetter: record => GetAnalyticsSource(record).PlottoSeed,
            sourceObjectName: AnalyticsSource,
            sourceMemberName: nameof(AnalyticsDemoSource.PlottoSeed)));
        if (_htmlField is not null && _htmlField.Segments.Count == 0)
        {
            _htmlField.Segments = _allFields
                .Select((field, index) => (field, index))
                .Where(x => x.index != _htmlField.SourceFieldIndex && x.field is not IHtmlFieldLayoutTarget)
                .Select(x => new HtmlFieldSegmentConfiguration
                {
                    SourceFieldIndex = x.index,
                    Enabled = x.index <= 2,
                    AbbreviatedHeaderOverride = string.Empty,
                    AddLineBreakAfter = true,
                    WordWrap = true
                })
                .ToList();
        }

        var cellEditItem = new MenuItem
        {
            Header = "Cell edit",
            InputGestureText = "F2"
        };
        cellEditItem.Click += (_, _) => DemoGrid.EditCurrentCell();

        var cancelCellEditItem = new MenuItem
        {
            Header = "Cancel cell edit",
            InputGestureText = "Esc"
        };
        cancelCellEditItem.Click += (_, _) => DemoGrid.CancelCurrentCellEdit();

        var copyItem = new MenuItem
        {
            Header = "Copy",
            InputGestureText = "Ctrl+C"
        };
        copyItem.Click += (_, _) => DemoGrid.CopyToClipboard();

        var cutItem = new MenuItem
        {
            Header = "Cut",
            InputGestureText = "Ctrl+X"
        };
        cutItem.Click += (_, _) => DemoGrid.CutToClipboard();

        var pasteItem = new MenuItem
        {
            Header = "Paste",
            InputGestureText = "Ctrl+V"
        };
        pasteItem.Click += (_, _) => DemoGrid.PasteFromClipboard();

        var clearItem = new MenuItem
        {
            Header = "Delete (clear)",
            InputGestureText = "Delete"
        };
        clearItem.Click += (_, _) => DemoGrid.ClearCells();

        var fillDownItem = new MenuItem
        {
            Header = "Fill _down",
            InputGestureText = "Ctrl+D",
            ToolTip = "Copy the top selected cell in each field to the other selected records in that field."
        };
        fillDownItem.Click += (_, _) => DemoGrid.FillSelectionDown();

        var incrementalDownNoPadItem = new MenuItem
        {
            Header = "Incremental down (no pad)",
            InputGestureText = "Ctrl+I",
            ToolTip = "Increment the last integer in each selected field without zero padding."
        };
        incrementalDownNoPadItem.Click += (_, _) => DemoGrid.FillSelectionIncrementalDown(zeroPad: false);
        var incrementalDownPadItem = new MenuItem
        {
            Header = "Incremental down (zero pad)",
            InputGestureText = "Ctrl+Shift+I",
            ToolTip = "Increment the last integer in each selected field and keep zero padding."
        };
        incrementalDownPadItem.Click += (_, _) => DemoGrid.FillSelectionIncrementalDown(zeroPad: true);
        var incrementalDownKeepPadItem = new MenuItem
        {
            Header = "Incremental down (keep pad)",
            InputGestureText = "Ctrl+Alt+I",
            ToolTip = "Increment the last integer in each selected field and keep source padding width."
        };
        incrementalDownKeepPadItem.Click += (_, _) => DemoGrid.FillSelectionIncrementalDownKeepPadding();

        DemoGrid.CellContextMenu = new ContextMenu
        {
            Items =
            {
                cellEditItem,
                cancelCellEditItem,
                new Separator(),
                copyItem,
                cutItem,
                pasteItem,
                clearItem,
                new Separator(),
                fillDownItem,
                incrementalDownNoPadItem,
                incrementalDownPadItem,
                incrementalDownKeepPadItem,
                new Separator(),
                new MenuItem { Header = "Second item" },
            },
        };

        for (var i = 1; i <= 100; i++)
        {
            var resultIndex = ((i - 1) / 12) % 3;
            var statusIndex = ((i - 1) / 4) % 3;
            var (resultKey, resultDisplay) = resultIndex switch
            {
                0 => ("H", "High"),
                1 => ("M", "Medium"),
                _ => ("L", "Low")
            };
            var (statusKey, statusDisplay) = statusIndex switch
            {
                0 => ("N", "None"),
                1 => ("T", "Tentative"),
                _ => ("A", "Approved")
            };
            var primary = new PrimaryDemoSource
            {
                Id = i,
                Name = $"Item {i:000}",
                ResultFlagKey = resultKey,
                ResultFlagDisplay = resultDisplay,
                ReviewStatusKey = statusKey,
                ReviewStatusDisplay = statusDisplay,
                Active = i % 5 == 0,
                HtmlSnippet = i % 3 == 0
                    ? $"<table><tr><th>R{i}</th><th>Q{i % 4}</th></tr><tr><td>{i * 2}</td><td><b>{Math.Round(i / 3.0, 2)}</b></td></tr></table>"
                    : $"<b>Record {i}</b> has <i>formatted</i> text",
                Graphic = CreatePathMarkupDemo(i)
            };
            var analytics = new AnalyticsDemoSource
            {
                Score = Math.Round(40 + Random.Shared.NextDouble() * 60, 2),
                PlottoSeed = i
            };

            DemoGrid.Records.Add(new Dictionary<string, object>
            {
                [PrimarySource] = primary,
                [AnalyticsSource] = analytics
            });
        }

        ApplyPersistedPropertyViews();
        ApplyPersistedGridLayout(DemoGrid, DemoGridLayoutKey);
        DemoGrid.InvalidateVisual();
    }

    private void RegisterField(IGriddoFieldView field)
    {
        if (field is ComposedHtmlFieldView htmlField)
        {
            htmlField.SourceFieldIndex = _allFields.Count;
        }
        _allFields.Add(field);
        DemoGrid.Fields.Add(field);
    }

    /// <summary>True when every record’s formatted value for this field is null/whitespace (no records ⇒ not empty).</summary>
    private static bool IsFieldEmptyForAllRecords(global::Griddo.Grid.Griddo grid, IGriddoFieldView field)
    {
        if (grid.Records.Count == 0)
        {
            return false;
        }

        foreach (var record in grid.Records)
        {
            var formatted = field.FormatValue(field.GetValue(record));
            if (!string.IsNullOrWhiteSpace(formatted))
            {
                return false;
            }
        }

        return true;
    }

    private static void HideEmptyFields(global::Griddo.Grid.Griddo grid, IReadOnlyList<IGriddoFieldView> registry)
    {
        var toHide = new List<IGriddoFieldView>();
        foreach (var col in registry)
        {
            if (!grid.Fields.Contains(col))
            {
                continue;
            }

            if (IsFieldEmptyForAllRecords(grid, col))
            {
                toHide.Add(col);
            }
        }

        foreach (var col in toHide)
        {
            if (grid.Fields.Count <= 1)
            {
                break;
            }

            if (grid.Fields.Contains(col))
            {
                grid.Fields.Remove(col);
            }
        }

        grid.InvalidateMeasure();
        grid.InvalidateVisual();
    }

    private static void ShowAllFields(global::Griddo.Grid.Griddo grid, IReadOnlyList<IGriddoFieldView> registry)
    {
        foreach (var col in registry)
        {
            ToggleFieldVisibility(grid, registry, col, true);
        }
    }

    private static void ShowAllFieldsExceptEmpty(global::Griddo.Grid.Griddo grid, IReadOnlyList<IGriddoFieldView> registry)
    {
        ShowAllFields(grid, registry);
        HideEmptyFields(grid, registry);
    }

    private static void ToggleFieldVisibility(
        global::Griddo.Grid.Griddo grid,
        IReadOnlyList<IGriddoFieldView> registry,
        IGriddoFieldView field,
        bool? shouldShow)
    {
        var isVisible = grid.Fields.Contains(field);
        if (shouldShow == isVisible)
        {
            return;
        }

        if (shouldShow != true)
        {
            if (grid.Fields.Count <= 1)
            {
                return;
            }

            grid.Fields.Remove(field);
        }
        else
        {
            var insertIndex = 0;
            foreach (var orderedField in registry)
            {
                if (ReferenceEquals(orderedField, field))
                {
                    break;
                }

                if (grid.Fields.Contains(orderedField))
                {
                    insertIndex++;
                }
            }

            insertIndex = Math.Clamp(insertIndex, 0, grid.Fields.Count);
            grid.Fields.Insert(insertIndex, field);
        }

        grid.InvalidateMeasure();
        grid.InvalidateVisual();
    }

    private static List<IGriddoFieldView> GetSelectedVisibleFields(global::Griddo.Grid.Griddo grid)
    {
        var selectedIndices = grid.SelectedCells
            .Select(c => c.FieldIndex)
            .Where(index => index >= 0 && index < grid.Fields.Count)
            .Distinct()
            .OrderBy(index => index)
            .ToList();

        var selectedFields = new List<IGriddoFieldView>(selectedIndices.Count);
        foreach (var index in selectedIndices)
        {
            selectedFields.Add(grid.Fields[index]);
        }

        return selectedFields;
    }

    private static string GetFieldVisibilityLabel(IGriddoFieldView field)
    {
        var sourceObjectName = field is IGriddoFieldSourceObject sourceObj ? sourceObj.SourceObjectName : string.Empty;
        if (string.IsNullOrWhiteSpace(sourceObjectName))
        {
            return field.Header;
        }

        return $"{sourceObjectName}.{field.Header}";
    }

    private static PrimaryDemoSource GetPrimarySource(object record)
    {
        if (record is IReadOnlyDictionary<string, object> map
            && map.TryGetValue(PrimarySource, out var source)
            && source is PrimaryDemoSource primary)
        {
            return primary;
        }

        throw new InvalidOperationException($"Record does not contain source '{PrimarySource}'.");
    }

    private static AnalyticsDemoSource GetAnalyticsSource(object record)
    {
        if (record is IReadOnlyDictionary<string, object> map
            && map.TryGetValue(AnalyticsSource, out var source)
            && source is AnalyticsDemoSource analytics)
        {
            return analytics;
        }

        throw new InvalidOperationException($"Record does not contain source '{AnalyticsSource}'.");
    }

    /// <summary>Five calibration standards; Y values vary slightly by record seed. Fit mode cycles Linear / LinearThroughOrigin / Quadratic / QuadraticThroughOrigin by seed.</summary>
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
    /// Record-dependent mini drawings (~60×56 logical coords); scaled into the cell by Griddo.
    /// Cycles <see cref="PathMarkupVariants"/> so adjacent records usually differ.
    /// </summary>
    private static Geometry CreatePathMarkupDemo(int seed)
    {
        var pathData = PathMarkupVariants[seed % PathMarkupVariants.Length];
        var g = Geometry.Parse(pathData);
        g.Freeze();
        return g;
    }

    private void OpenFieldConfigurator_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        OpenFieldConfigurator(DemoGrid, _allFields, DemoGridLayoutKey);
    }

    private void OpenFieldConfigurator(
        global::Griddo.Grid.Griddo grid,
        IReadOnlyList<IGriddoFieldView> fieldRegistry,
        string layoutKey)
    {
        var records = FieldMetadataBuilder.BuildRecordsFromGrid(grid, fieldRegistry);
        for (var sourceIndex = 0; sourceIndex < fieldRegistry.Count; sourceIndex++)
        {
            var registryField = fieldRegistry[sourceIndex];
            var visibleGridIndex = grid.Fields.IndexOf(registryField);
            if (visibleGridIndex < 0)
            {
                continue;
            }

            var record = records.FirstOrDefault(r => r.SourceFieldIndex == sourceIndex);
            if (record is null)
            {
                continue;
            }

            // BuildRecordsFromGrid uses field definition width; capture current live grid width overrides too.
            record.Width = grid.GetLogicalFieldWidth(visibleGridIndex);
        }
        var shouldPersistPropertyViews = ReferenceEquals(fieldRegistry, _allFields);
        if (shouldPersistPropertyViews)
        {
            ApplyPersistedRecordMetadata(records);
        }
        var frozenFields = grid.FixedFieldCount;
        var frozenRecords = grid.FixedRecordCount;
        var initialOptions = new FieldChooserGeneralOptions
        {
            RecordThickness = (int)Math.Round(grid.UniformRecordHeight),
            VisibleRecordCount = grid.VisibleRecordCount,
            ShowSelectionColor = grid.ShowCellSelectionColoring,
            ShowCurrentCellRect = grid.ShowCurrentCellColor,
            ShowRecordSelectionColor = grid.ShowRecordHeaderSelectionColoring,
            ShowColSelectionColor = grid.ShowFieldHeaderSelectionColoring,
            ShowEditCellRect = grid.ShowEditCellColor,
            ShowSortingIndicators = grid.ShowSortingIndicators,
            ShowHorizontalScrollBar = grid.ShowHorizontalScrollBar,
            ShowVerticalScrollBar = grid.ShowVerticalScrollBar,
            IsTransposed = grid.IsTransposed,
            ImmediatePlottoEdit = grid.HostedPlotDirectEditOnMouseDown
        };

        // Persisted configurator state should not depend on which source grid opened the dialog.
        var dlg = new FieldConfigurator(records, frozenFields, frozenRecords, initialOptions) { Owner = this };
        if (ReferenceEquals(fieldRegistry, _allFields))
        {
            ApplyPersistedGridLayoutForRegistry(dlg.ConfigFieldsGrid, dlg.FieldHeaderRegistry, ConfigFieldsGridLayoutKey);
            ApplyPersistedGridLayoutForRegistry(dlg.ConfigGeneralSettingsGrid, dlg.GeneralFieldHeaderRegistry, ConfigGeneralGridLayoutKey);
        }
        dlg.TargetSourceGrid = grid;
        dlg.ApplyToSourceGrid = (r, fc, fr, go) =>
        {
            FieldChooserGridApplier.Apply(grid, r, fc, fr, go, fieldRegistry);
            if (shouldPersistPropertyViews)
            {
                PersistPropertyViews(r);
            }
            PersistGridLayout(grid, layoutKey, r, fc, fr, go);
            PersistGridLayoutFromCurrentGrid(dlg.ConfigFieldsGrid, ConfigFieldsGridLayoutKey, dlg.FieldHeaderRegistry);
            PersistGridLayoutFromCurrentGrid(dlg.ConfigGeneralSettingsGrid, ConfigGeneralGridLayoutKey, dlg.GeneralFieldHeaderRegistry);
        };
        dlg.FieldHeaderMenuHandler = (g, registry, ev) => OnFieldHeaderRightClick(g, registry, ev);
        grid.ClearCellSelection();
        dlg.ShowDialog();
    }

    private static bool IsGeneralConfigRegistry(IReadOnlyList<IGriddoFieldView> fieldRegistry)
    {
        if (fieldRegistry.Count != 3)
        {
            return false;
        }

        return string.Equals(fieldRegistry[0].Header, "Category", StringComparison.OrdinalIgnoreCase)
            && string.Equals(fieldRegistry[1].Header, "Setting", StringComparison.OrdinalIgnoreCase)
            && string.Equals(fieldRegistry[2].Header, "Value", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveLayoutKey(global::Griddo.Grid.Griddo targetGrid, IReadOnlyList<IGriddoFieldView> fieldRegistry)
    {
        if (ReferenceEquals(targetGrid, DemoGrid))
        {
            return DemoGridLayoutKey;
        }

        return IsGeneralConfigRegistry(fieldRegistry) ? ConfigGeneralGridLayoutKey : ConfigFieldsGridLayoutKey;
    }

    private void ApplyPersistedGridLayoutForRegistry(
        global::Griddo.Grid.Griddo grid,
        IReadOnlyList<IGriddoFieldView> registry,
        string layoutKey)
    {
        if (!_gridLayoutStore.TryGet(layoutKey, out var layout))
        {
            return;
        }

        var records = FieldMetadataBuilder.BuildRecordsFromGrid(grid, registry);
        var byIndex = layout.Fields.ToDictionary(c => c.SourceFieldIndex);
        foreach (var record in records)
        {
            if (!byIndex.TryGetValue(record.SourceFieldIndex, out var state))
            {
                continue;
            }

            record.Fill = state.Fill;
            record.Visible = state.Visible;
            record.Width = Math.Max(28, state.Width);
            record.SortPriority = Math.Max(0, state.SortPriority);
            record.SortAscending = state.SortAscending;
        }
        records = ReorderRecordsByPersistedLayout(records, layout.Fields);

        var options = new FieldChooserGeneralOptions
        {
            RecordThickness = layout.RecordThickness,
            VisibleRecordCount = layout.VisibleRecordCount,
            ShowSelectionColor = layout.ShowSelectionColor,
            ShowCurrentCellRect = layout.ShowCurrentCellRect,
            ShowRecordSelectionColor = layout.ShowRecordSelectionColor,
            ShowColSelectionColor = layout.ShowColSelectionColor,
            ShowEditCellRect = layout.ShowEditCellRect,
            ShowSortingIndicators = layout.ShowSortingIndicators,
            ShowHorizontalScrollBar = layout.ShowHorizontalScrollBar,
            ShowVerticalScrollBar = layout.ShowVerticalScrollBar,
            IsTransposed = layout.IsTransposed,
            ImmediatePlottoEdit = layout.ImmediatePlottoEdit
        };

        FieldChooserGridApplier.Apply(
            grid,
            records,
            layout.FrozenFields,
            layout.FrozenRecords,
            options,
            registry,
            new HashSet<int>(byIndex.Keys));

        if (IsConfiguratorInternalLayoutKey(layoutKey))
        {
            grid.ShowFieldHeaderSelectionColoring = false;
            grid.ShowRecordHeaderSelectionColoring = false;
        }
    }

    private void ApplyPersistedPropertyViews()
    {
        for (var sourceFieldIndex = 0; sourceFieldIndex < _allFields.Count; sourceFieldIndex++)
        {
            var field = _allFields[sourceFieldIndex];
            if (field is not IGriddoFieldSourceMember sourceMember
                || string.IsNullOrWhiteSpace(sourceMember.SourceMemberName))
            {
                continue;
            }

            var sourceClassName = ResolveSourceClassName(field);
            if (string.IsNullOrWhiteSpace(sourceClassName))
            {
                continue;
            }

            var propertyKey = ResolvePropertyViewKeyForSourceField(sourceFieldIndex, sourceMember.SourceMemberName);
            if (!_viewStore.TryGet(sourceClassName, propertyKey, out var definition)
                && !_viewStore.TryGet(sourceClassName, sourceMember.SourceMemberName, out definition))
            {
                continue;
            }

            if (field is IGriddoFieldFormatView formatView)
            {
                formatView.FormatString = definition.StringFormat ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(definition.Header))
            {
                field.Header = definition.Header;
            }

            if (field is IGriddoFieldTitleView titleView)
            {
                titleView.AbbreviatedHeader = definition.AbbreviatedHeader ?? string.Empty;
            }

            if (field is IGriddoFieldDescriptionView descriptionView)
            {
                descriptionView.Description = definition.Description ?? string.Empty;
            }

            if (field is IGriddoFieldFontView fontView)
            {
                fontView.FontSize = Math.Max(0, definition.FontSize);
                fontView.FontStyleName = definition.FontStyle ?? string.Empty;
            }
            if (field is IGriddoFieldWrapView wrapView)
            {
                wrapView.NoWrap = definition.NoWrap;
            }

            if (field is IGriddoFieldColorView colorView)
            {
                colorView.ForegroundColor = definition.ForegroundColor ?? string.Empty;
                colorView.BackgroundColor = definition.BackgroundColor ?? string.Empty;
            }
        }
    }

    private void PersistPropertyViews(IReadOnlyList<FieldEditRecord> records)
    {
        foreach (var record in records)
        {
            var sourceClassName = ResolveSourceClassName(record.SourceObjectName);
            var propertyName = ResolvePropertyViewKeyForRecord(record);
            if (string.IsNullOrWhiteSpace(sourceClassName) || string.IsNullOrWhiteSpace(propertyName))
            {
                continue;
            }

            var definition = new PropertyViewConfiguration
            {
                SourceClassName = sourceClassName,
                PropertyName = propertyName,
                Header = record.Title ?? string.Empty,
                AbbreviatedHeader = record.AbbreviatedTitle ?? string.Empty,
                Description = record.Description ?? string.Empty,
                StringFormat = record.FormatString ?? string.Empty,
                FontSize = Math.Max(0, record.FontSize),
                FontStyle = record.FontStyleName ?? string.Empty,
                NoWrap = record.NoWrap,
                ForegroundColor = record.ForegroundColor ?? string.Empty,
                BackgroundColor = record.BackgroundColor ?? string.Empty
            };
            _viewStore.Set(definition);
        }

        _viewStore.Save();
    }

    private void ApplyPersistedRecordMetadata(IReadOnlyList<FieldEditRecord> records)
    {
        foreach (var record in records)
        {
            var sourceClassName = ResolveSourceClassName(record.SourceObjectName);
            var propertyName = ResolvePropertyViewKeyForRecord(record);
            if (string.IsNullOrWhiteSpace(sourceClassName) || string.IsNullOrWhiteSpace(propertyName))
            {
                continue;
            }

            var fallbackPropertyName = ResolveSourcePropertyName(record);
            if (!_viewStore.TryGet(sourceClassName, propertyName, out var definition)
                && !_viewStore.TryGet(sourceClassName, fallbackPropertyName, out definition))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(definition.Header))
            {
                record.Title = definition.Header;
            }

            if (!string.IsNullOrWhiteSpace(definition.AbbreviatedHeader))
            {
                record.AbbreviatedTitle = definition.AbbreviatedHeader;
            }

            if (!string.IsNullOrWhiteSpace(definition.Description))
            {
                record.Description = definition.Description;
            }

            record.FormatString = definition.StringFormat ?? string.Empty;
            record.FontSize = Math.Max(0, definition.FontSize);
            record.FontStyleName = definition.FontStyle ?? string.Empty;
            record.NoWrap = definition.NoWrap;
            record.ForegroundColor = definition.ForegroundColor ?? string.Empty;
            record.BackgroundColor = definition.BackgroundColor ?? string.Empty;
        }
    }

    private void ApplyPersistedGridLayout(global::Griddo.Grid.Griddo grid, string gridKey)
    {
        if (!_gridLayoutStore.TryGet(gridKey, out var layout))
        {
            return;
        }

        foreach (var plot in layout.PlotFields)
        {
            if (plot.SourceFieldIndex < 0 || plot.SourceFieldIndex >= _allFields.Count)
            {
                continue;
            }

            if (_allFields[plot.SourceFieldIndex] is not IPlotFieldLayoutTarget target)
            {
                continue;
            }

            target.TitleSelection = plot.TitleSelection ?? string.Empty;
            target.ShowXAxis = plot.ShowXAxis;
            target.ShowYAxis = plot.ShowYAxis;
            target.XAxis = plot.XAxis ?? string.Empty;
            target.YAxis = plot.YAxis ?? string.Empty;
            target.XAxisTitle = plot.XAxisTitle ?? string.Empty;
            target.YAxisTitle = plot.YAxisTitle ?? string.Empty;
            target.Label = plot.Label ?? string.Empty;
            target.XAxisUnit = plot.XAxisUnit ?? string.Empty;
            target.YAxisUnit = plot.YAxisUnit ?? string.Empty;
            target.XAxisLabelPrecision = Math.Clamp(plot.XAxisLabelPrecision, 0, 10);
            target.YAxisLabelPrecision = Math.Clamp(plot.YAxisLabelPrecision, 0, 10);
        }
        foreach (var html in layout.HtmlFields)
        {
            if (html.SourceFieldIndex < 0 || html.SourceFieldIndex >= _allFields.Count)
            {
                continue;
            }

            if (_allFields[html.SourceFieldIndex] is not IHtmlFieldLayoutTarget target)
            {
                continue;
            }

            target.FontFamilyName = html.FontFamilyName ?? string.Empty;
            target.FontSize = Math.Max(0, html.FontSize);
            target.FontStyleName = html.FontStyleName ?? string.Empty;
            target.Segments = html.Segments
                .Select(s => new HtmlFieldSegmentConfiguration
                {
                    SourceFieldIndex = s.SourceFieldIndex,
                    Enabled = s.Enabled,
                    AbbreviatedHeaderOverride = s.AbbreviatedHeaderOverride ?? string.Empty,
                    AddLineBreakAfter = s.AddLineBreakAfter,
                    WordWrap = s.WordWrap
                })
                .ToList();
        }
        var records = FieldMetadataBuilder.BuildRecordsFromGrid(grid, _allFields);
        ApplyPersistedRecordMetadata(records);
        var byIndex = layout.Fields.ToDictionary(c => c.SourceFieldIndex);
        foreach (var record in records)
        {
            if (!byIndex.TryGetValue(record.SourceFieldIndex, out var state))
            {
                continue;
            }

            record.Fill = state.Fill;
            record.Visible = state.Visible;
            record.Width = Math.Max(28, state.Width);
            record.SortPriority = Math.Max(0, state.SortPriority);
            record.SortAscending = state.SortAscending;
        }
        records = ReorderRecordsByPersistedLayout(records, layout.Fields);

        var options = new FieldChooserGeneralOptions
        {
            RecordThickness = layout.RecordThickness,
            VisibleRecordCount = layout.VisibleRecordCount,
            ShowSelectionColor = layout.ShowSelectionColor,
            ShowCurrentCellRect = layout.ShowCurrentCellRect,
            ShowRecordSelectionColor = layout.ShowRecordSelectionColor,
            ShowColSelectionColor = layout.ShowColSelectionColor,
            ShowEditCellRect = layout.ShowEditCellRect,
            ShowSortingIndicators = layout.ShowSortingIndicators,
            ShowHorizontalScrollBar = layout.ShowHorizontalScrollBar,
            ShowVerticalScrollBar = layout.ShowVerticalScrollBar,
            IsTransposed = layout.IsTransposed,
            ImmediatePlottoEdit = layout.ImmediatePlottoEdit
        };
        FieldChooserGridApplier.Apply(
            grid,
            records,
            layout.FrozenFields,
            layout.FrozenRecords,
            options,
            _allFields,
            new HashSet<int>(byIndex.Keys));
    }

    private void PersistGridLayout(
        global::Griddo.Grid.Griddo grid,
        string gridKey,
        IReadOnlyList<FieldEditRecord> records,
        int frozenFields,
        int frozenRecords,
        FieldChooserGeneralOptions options)
    {
        var definition = new GridConfiguration
        {
            Key = gridKey,
            RecordThickness = options.RecordThickness,
            VisibleRecordCount = options.VisibleRecordCount,
            FrozenFields = frozenFields,
            FrozenRecords = frozenRecords,
            ShowSelectionColor = options.ShowSelectionColor,
            ShowCurrentCellRect = options.ShowCurrentCellRect,
            ShowRecordSelectionColor = options.ShowRecordSelectionColor,
            ShowColSelectionColor = options.ShowColSelectionColor,
            ShowEditCellRect = options.ShowEditCellRect,
            ShowSortingIndicators = options.ShowSortingIndicators,
            ShowHorizontalScrollBar = options.ShowHorizontalScrollBar,
            ShowVerticalScrollBar = options.ShowVerticalScrollBar,
            IsTransposed = options.IsTransposed,
            ImmediatePlottoEdit = options.ImmediatePlottoEdit,
            Fields = records.Select(r => new FieldConfiguration
            {
                SourceFieldIndex = r.SourceFieldIndex,
                Fill = r.Fill,
                Visible = r.Visible,
                Width = r.Width,
                SortPriority = r.SortPriority,
                SortAscending = r.SortAscending
            }).ToList(),
            PlotFields = _allFields
                .Select((field, index) => (field, index))
                .Where(static x => x.field is IPlotFieldLayoutTarget)
                .Select(x =>
                {
                    var p = (IPlotFieldLayoutTarget)x.field;
                    return new PlotFieldConfiguration
                    {
                        SourceFieldIndex = x.index,
                        TitleSelection = p.TitleSelection ?? string.Empty,
                        ShowXAxis = p.ShowXAxis,
                        ShowYAxis = p.ShowYAxis,
                        XAxis = p.XAxis ?? string.Empty,
                        YAxis = p.YAxis ?? string.Empty,
                        XAxisTitle = p.XAxisTitle ?? string.Empty,
                        YAxisTitle = p.YAxisTitle ?? string.Empty,
                        Label = p.Label ?? string.Empty,
                        XAxisUnit = p.XAxisUnit ?? string.Empty,
                        YAxisUnit = p.YAxisUnit ?? string.Empty,
                        XAxisLabelPrecision = Math.Clamp(p.XAxisLabelPrecision, 0, 10),
                        YAxisLabelPrecision = Math.Clamp(p.YAxisLabelPrecision, 0, 10)
                    };
                })
                .ToList(),
            HtmlFields = _allFields
                .Select((field, index) => (field, index))
                .Where(static x => x.field is IHtmlFieldLayoutTarget)
                .Select(x =>
                {
                    var h = (IHtmlFieldLayoutTarget)x.field;
                    return new HtmlFieldConfiguration
                    {
                        SourceFieldIndex = x.index,
                        FontFamilyName = h.FontFamilyName ?? string.Empty,
                        FontSize = Math.Max(0, h.FontSize),
                        FontStyleName = h.FontStyleName ?? string.Empty,
                        Segments = h.Segments
                            .Select(s => new HtmlFieldSegmentConfiguration
                            {
                                SourceFieldIndex = s.SourceFieldIndex,
                                Enabled = s.Enabled,
                                AbbreviatedHeaderOverride = s.AbbreviatedHeaderOverride ?? string.Empty,
                                AddLineBreakAfter = s.AddLineBreakAfter,
                                WordWrap = s.WordWrap
                            })
                            .ToList()
                    };
                })
                .ToList()
        };

        _gridLayoutStore.Set(definition);
        _gridLayoutStore.Save();
    }

    private void PersistGridLayoutFromCurrentGrid(
        global::Griddo.Grid.Griddo grid,
        string gridKey,
        IReadOnlyList<IGriddoFieldView> registry)
    {
        var records = BuildPersistedOrderRecordsFromCurrentGrid(grid, registry);
        for (var sourceIndex = 0; sourceIndex < registry.Count; sourceIndex++)
        {
            var registryField = registry[sourceIndex];
            var visibleGridIndex = grid.Fields.IndexOf(registryField);
            if (visibleGridIndex < 0)
            {
                continue;
            }

            var record = records.FirstOrDefault(r => r.SourceFieldIndex == sourceIndex);
            if (record is null)
            {
                continue;
            }

            record.Width = grid.GetLogicalFieldWidth(visibleGridIndex);
        }
        var options = new FieldChooserGeneralOptions
        {
            RecordThickness = (int)Math.Round(grid.UniformRecordHeight),
            VisibleRecordCount = grid.VisibleRecordCount,
            ShowSelectionColor = grid.ShowCellSelectionColoring,
            ShowCurrentCellRect = grid.ShowCurrentCellColor,
            ShowRecordSelectionColor = grid.ShowRecordHeaderSelectionColoring,
            ShowColSelectionColor = grid.ShowFieldHeaderSelectionColoring,
            ShowEditCellRect = grid.ShowEditCellColor,
            ShowSortingIndicators = grid.ShowSortingIndicators,
            ShowHorizontalScrollBar = grid.ShowHorizontalScrollBar,
            ShowVerticalScrollBar = grid.ShowVerticalScrollBar,
            IsTransposed = grid.IsTransposed,
            ImmediatePlottoEdit = grid.HostedPlotDirectEditOnMouseDown
        };
        if (IsConfiguratorInternalLayoutKey(gridKey))
        {
            options.ShowRecordSelectionColor = false;
            options.ShowColSelectionColor = false;
        }

        PersistGridLayout(grid, gridKey, records, grid.FixedFieldCount, grid.FixedRecordCount, options);
    }

    private static List<FieldEditRecord> BuildPersistedOrderRecordsFromCurrentGrid(
        global::Griddo.Grid.Griddo grid,
        IReadOnlyList<IGriddoFieldView> registry)
    {
        var records = FieldMetadataBuilder.BuildRecordsFromGrid(grid, registry);
        if (records.Count == 0 || registry.Count == 0)
        {
            return records;
        }

        var bySourceIndex = records.ToDictionary(r => r.SourceFieldIndex);
        var ordered = new List<FieldEditRecord>(records.Count);
        var added = new HashSet<int>();

        foreach (var visibleField in grid.Fields)
        {
            var sourceIndex = -1;
            for (var i = 0; i < registry.Count; i++)
            {
                if (ReferenceEquals(registry[i], visibleField))
                {
                    sourceIndex = i;
                    break;
                }
            }

            if (sourceIndex < 0
                || !bySourceIndex.TryGetValue(sourceIndex, out var record)
                || !added.Add(sourceIndex))
            {
                continue;
            }

            ordered.Add(record);
        }

        for (var i = 0; i < registry.Count; i++)
        {
            if (!bySourceIndex.TryGetValue(i, out var record) || !added.Add(i))
            {
                continue;
            }

            ordered.Add(record);
        }

        return ordered;
    }

    private static List<FieldEditRecord> ReorderRecordsByPersistedLayout(
        List<FieldEditRecord> records,
        IReadOnlyList<FieldConfiguration> persistedFields)
    {
        if (records.Count == 0 || persistedFields.Count == 0)
        {
            return records;
        }

        var bySourceIndex = records.ToDictionary(r => r.SourceFieldIndex);
        var ordered = new List<FieldEditRecord>(records.Count);
        var added = new HashSet<int>();

        foreach (var persisted in persistedFields)
        {
            if (!bySourceIndex.TryGetValue(persisted.SourceFieldIndex, out var record)
                || !added.Add(persisted.SourceFieldIndex))
            {
                continue;
            }

            ordered.Add(record);
        }

        foreach (var record in records)
        {
            if (added.Add(record.SourceFieldIndex))
            {
                ordered.Add(record);
            }
        }

        return ordered;
    }

    private bool IsConfiguratorInternalLayoutKey(string layoutKey) =>
        string.Equals(layoutKey, ConfigFieldsGridLayoutKey, StringComparison.Ordinal)
        || string.Equals(layoutKey, ConfigGeneralGridLayoutKey, StringComparison.Ordinal);

    private string ResolveSourcePropertyName(FieldEditRecord record)
    {
        if (record.SourceFieldIndex >= 0
            && record.SourceFieldIndex < _allFields.Count
            && _allFields[record.SourceFieldIndex] is IGriddoFieldSourceMember sourceMember
            && !string.IsNullOrWhiteSpace(sourceMember.SourceMemberName))
        {
            return sourceMember.SourceMemberName;
        }

        return record.PropertyName;
    }

    private string ResolvePropertyViewKeyForRecord(FieldEditRecord record)
    {
        if (record.SourceFieldIndex >= 0 && record.SourceFieldIndex < _allFields.Count)
        {
            var sourceMember = (_allFields[record.SourceFieldIndex] as IGriddoFieldSourceMember)?.SourceMemberName ?? string.Empty;
            return ResolvePropertyViewKeyForSourceField(record.SourceFieldIndex, sourceMember);
        }

        return ResolveSourcePropertyName(record);
    }

    private string ResolvePropertyViewKeyForSourceField(int sourceFieldIndex, string sourceMemberName)
    {
        if (sourceFieldIndex < 0 || sourceFieldIndex >= _allFields.Count)
        {
            return sourceMemberName;
        }

        if (string.IsNullOrWhiteSpace(sourceMemberName))
        {
            return sourceMemberName;
        }

        var sourceObjectName = (_allFields[sourceFieldIndex] as IGriddoFieldSourceObject)?.SourceObjectName ?? string.Empty;
        var hasDuplicateMember = _allFields
            .Select((field, index) => (field, index))
            .Any(x =>
                x.index != sourceFieldIndex
                && x.field is IGriddoFieldSourceMember otherMember
                && string.Equals(otherMember.SourceMemberName, sourceMemberName, StringComparison.Ordinal)
                && string.Equals(
                    (x.field as IGriddoFieldSourceObject)?.SourceObjectName ?? string.Empty,
                    sourceObjectName,
                    StringComparison.Ordinal));
        return hasDuplicateMember ? $"{sourceMemberName}#{sourceFieldIndex}" : sourceMemberName;
    }

    private static string ResolveSourceClassName(IGriddoFieldView field)
    {
        if (field is not IGriddoFieldSourceObject sourceObject)
        {
            return string.Empty;
        }

        return ResolveSourceClassName(sourceObject.SourceObjectName);
    }

    private static string ResolveSourceClassName(string sourceObjectName)
    {
        if (string.Equals(sourceObjectName, PrimarySource, StringComparison.OrdinalIgnoreCase))
        {
            return nameof(PrimaryDemoSource);
        }

        if (string.Equals(sourceObjectName, AnalyticsSource, StringComparison.OrdinalIgnoreCase))
        {
            return nameof(AnalyticsDemoSource);
        }

        return string.Empty;
    }

    private sealed class ComposedHtmlFieldView : IGriddoFieldView, IGriddoFieldDescriptionView, IGriddoFieldSourceMember, IGriddoFieldSourceObject, IGriddoFieldTitleView, IGriddoFieldFontView, IGriddoFieldWrapView, IHtmlFieldLayoutTarget
    {
        private readonly Func<IReadOnlyList<IGriddoFieldView>> _allFieldsAccessor;

        public ComposedHtmlFieldView(
            string header,
            double width,
            string sourceObjectName,
            string sourceMemberName,
            Func<IReadOnlyList<IGriddoFieldView>> allFieldsAccessor)
        {
            Header = header;
            Width = width;
            SourceObjectName = sourceObjectName;
            SourceMemberName = sourceMemberName;
            _allFieldsAccessor = allFieldsAccessor;
        }

        public int SourceFieldIndex { get; set; } = -1;
        public string Header { get; set; }
        public string AbbreviatedHeader { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string FontFamilyName { get; set; } = string.Empty;
        public double Width { get; }
        public string ForegroundColor { get; set; } = string.Empty;
        public string BackgroundColor { get; set; } = string.Empty;
        public bool NoWrap { get; set; }
        public bool Fill { get; set; }
        public bool IsHtml => true;
        public TextAlignment ContentAlignment => TextAlignment.Left;
        public IGriddoCellEditor Editor => GriddoCellEditors.Text;
        public string SourceMemberName { get; }
        public string SourceObjectName { get; }
        public double FontSize { get; set; }
        public string FontStyleName { get; set; } = string.Empty;
        public List<HtmlFieldSegmentConfiguration> Segments { get; set; } = [];

        public object? GetValue(object recordSource)
        {
            var allFields = _allFieldsAccessor();
            if (allFields.Count == 0)
            {
                return string.Empty;
            }

            var enabledSegments = Segments
                .Where(s => s.Enabled)
                .OrderBy(s => s.SourceFieldIndex)
                .ToList();
            if (enabledSegments.Count == 0)
            {
                return string.Empty;
            }

            var rows = BuildTableRowsHtml(enabledSegments, allFields, recordSource);
            if (rows.Count == 0)
            {
                return string.Empty;
            }

            return $"<table>{string.Concat(rows)}</table>";
        }

        public bool TrySetValue(object recordSource, object? value)
        {
            _ = recordSource;
            _ = value;
            return false;
        }

        public string FormatValue(object? value) => value?.ToString() ?? string.Empty;

        private string BuildRowHtml(HtmlFieldSegmentConfiguration segment, IReadOnlyList<IGriddoFieldView> allFields, object recordSource)
        {
            if (!TryResolveField(segment.SourceFieldIndex, allFields, out var sourceField))
            {
                return string.Empty;
            }

            var label = ResolveLabel(sourceField, segment.AbbreviatedHeaderOverride);
            var rawValue = sourceField.GetValue(recordSource);
            var renderedValue = sourceField.FormatValue(rawValue);
            const string labelNoWrapStyle = " style=\"white-space:nowrap;\"";
            var valueWrapStyle = segment.WordWrap ? string.Empty : " style=\"white-space:nowrap;\"";
            var encodedLabel = WebUtility.HtmlEncode(label).Replace(" ", "\u00A0", StringComparison.Ordinal);
            var encodedRenderedValue = WebUtility.HtmlEncode(renderedValue);
            if (!segment.WordWrap)
            {
                encodedRenderedValue = encodedRenderedValue.Replace(" ", "\u00A0", StringComparison.Ordinal);
            }
            var valueHtml = sourceField.IsHtml
                ? BuildStyledHtml(rawValue?.ToString() ?? string.Empty, sourceField, forceVisibleTextColor: true)
                : BuildStyledText(encodedRenderedValue, sourceField, forceVisibleTextColor: true);
            return $"<td{labelNoWrapStyle}><b>{encodedLabel}</b></td><td{valueWrapStyle}>{valueHtml}</td>";
        }

        private string BuildDivHtml(
            HtmlFieldSegmentConfiguration segment,
            IReadOnlyList<IGriddoFieldView> allFields,
            object recordSource,
            bool appendPairSeparator)
        {
            if (!TryResolveField(segment.SourceFieldIndex, allFields, out var sourceField))
            {
                return string.Empty;
            }

            var label = ResolveLabel(sourceField, segment.AbbreviatedHeaderOverride);
            var renderedValue = sourceField.FormatValue(sourceField.GetValue(recordSource));
            var encodedLabel = WebUtility.HtmlEncode(label);
            var encodedValue = WebUtility.HtmlEncode(renderedValue);
            var breakPrefix = segment.AddLineBreakAfter ? "<br/>" : string.Empty;
            var pairSeparator = appendPairSeparator ? " \u00B7 " : string.Empty;
            const string labelNoWrapStyle = " style=\"white-space:nowrap;\"";
            var valueWrapStyle = segment.WordWrap ? string.Empty : " style=\"white-space:nowrap;\"";
            var valueHtml = sourceField.IsHtml
                ? BuildStyledHtml(sourceField.GetValue(recordSource)?.ToString() ?? string.Empty, sourceField, forceVisibleTextColor: true)
                : BuildStyledText(encodedValue, sourceField, forceVisibleTextColor: true);
            return $"{breakPrefix}<span{labelNoWrapStyle}><b>{encodedLabel}</b></span>: <span{valueWrapStyle}>{valueHtml}</span>{pairSeparator}";
        }

        private bool TryResolveField(int index, IReadOnlyList<IGriddoFieldView> allFields, out IGriddoFieldView field)
        {
            field = null!;
            if (index < 0 || index >= allFields.Count || index == SourceFieldIndex)
            {
                return false;
            }

            field = allFields[index];
            return true;
        }

        private static string ResolveLabel(IGriddoFieldView sourceField, string abbreviatedHeaderOverride)
        {
            if (!string.IsNullOrWhiteSpace(abbreviatedHeaderOverride))
            {
                return abbreviatedHeaderOverride;
            }

            return sourceField.Header;
        }

        private static string BuildStyledText(string encodedValue, IGriddoFieldView sourceField, bool forceVisibleTextColor)
        {
            var fallbackFg = (sourceField as IGriddoFieldColorView)?.ForegroundColor ?? string.Empty;
            var fallbackBg = (sourceField as IGriddoFieldColorView)?.BackgroundColor ?? string.Empty;
            var fgColor = fallbackFg;
            var bgColor = fallbackBg;
            if (sourceField is IGriddoFieldFontView fontView)
            {
                encodedValue = ApplyFontStyleMarkup(encodedValue, fontView.FontStyleName);
            }
            if (forceVisibleTextColor && string.IsNullOrWhiteSpace(fgColor) && !string.IsNullOrWhiteSpace(bgColor))
            {
                fgColor = ResolveContrastTextColor(bgColor);
            }
            var fg = string.IsNullOrWhiteSpace(fgColor) ? string.Empty : $" color=\"{WebUtility.HtmlEncode(fgColor)}\"";
            var bg = string.IsNullOrWhiteSpace(bgColor) ? string.Empty : $" style=\"background-color:{WebUtility.HtmlEncode(bgColor)};\"";
            if (fg.Length == 0 && bg.Length == 0)
            {
                return encodedValue;
            }

            if (fg.Length > 0)
            {
                return $"<font{fg}><span{bg}>{encodedValue}</span></font>";
            }

            return $"<span{bg}>{encodedValue}</span>";
        }

        private static string BuildStyledHtml(string rawHtml, IGriddoFieldView sourceField, bool forceVisibleTextColor)
        {
            var fallbackFg = (sourceField as IGriddoFieldColorView)?.ForegroundColor ?? string.Empty;
            var fallbackBg = (sourceField as IGriddoFieldColorView)?.BackgroundColor ?? string.Empty;
            var fgColor = fallbackFg;
            var bgColor = fallbackBg;
            if (forceVisibleTextColor && string.IsNullOrWhiteSpace(fgColor) && !string.IsNullOrWhiteSpace(bgColor))
            {
                fgColor = ResolveContrastTextColor(bgColor);
            }

            var styleParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(fgColor))
            {
                styleParts.Add($"color:{WebUtility.HtmlEncode(fgColor)}");
            }

            if (!string.IsNullOrWhiteSpace(bgColor))
            {
                styleParts.Add($"background-color:{WebUtility.HtmlEncode(bgColor)}");
            }

            if (styleParts.Count == 0)
            {
                return rawHtml;
            }

            return $"<span style=\"{string.Join(";", styleParts)}\">{rawHtml}</span>";
        }

        private static string ApplyFontStyleMarkup(string encodedValue, string styleName)
        {
            if (string.IsNullOrWhiteSpace(styleName))
            {
                return encodedValue;
            }

            var styled = encodedValue;
            if (styleName.Contains("Italic", StringComparison.OrdinalIgnoreCase))
            {
                styled = $"<i>{styled}</i>";
            }

            if (styleName.Contains("Bold", StringComparison.OrdinalIgnoreCase))
            {
                styled = $"<b>{styled}</b>";
            }

            return styled;
        }

        private static string ResolveContrastTextColor(string backgroundColor)
        {
            if (string.IsNullOrWhiteSpace(backgroundColor))
            {
                return "Black";
            }

            try
            {
                if (ColorConverter.ConvertFromString(backgroundColor) is Color color)
                {
                    var luminance = (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
                    return luminance < 140 ? "White" : "Black";
                }
            }
            catch
            {
                return "Black";
            }

            return "Black";
        }

        private List<string> BuildTableRowsHtml(
            IReadOnlyList<HtmlFieldSegmentConfiguration> enabledSegments,
            IReadOnlyList<IGriddoFieldView> allFields,
            object recordSource)
        {
            var rows = new List<string>();
            var currentCells = new List<string>();
            foreach (var segment in enabledSegments)
            {
                if (segment.AddLineBreakAfter && currentCells.Count > 0)
                {
                    rows.Add($"<tr>{string.Concat(currentCells)}</tr>");
                    currentCells.Clear();
                }

                var cells = BuildRowHtml(segment, allFields, recordSource);
                if (!string.IsNullOrWhiteSpace(cells))
                {
                    currentCells.Add(cells);
                }
            }

            if (currentCells.Count > 0)
            {
                rows.Add($"<tr>{string.Concat(currentCells)}</tr>");
            }

            return rows;
        }
    }

    private sealed class HierarchicalMergeTextFieldView : IGriddoFieldView, IGriddoFieldDescriptionView, IGriddoFieldSourceMember, IGriddoFieldSourceObject, IGriddoFieldColorView, IGriddoRecordMergeBandView, IGriddoFieldSortValueView
    {
        private readonly Func<System.Collections.IList> _recordsAccessor;
        private readonly Func<object, string> _displayGetter;
        private readonly Func<object, string> _sortKeyGetter;
        private readonly IReadOnlyList<Func<object, string>> _mergeKeyGetters;

        public HierarchicalMergeTextFieldView(
            string header,
            double width,
            string sourceObjectName,
            string sourceMemberName,
            Func<System.Collections.IList> recordsAccessor,
            Func<object, string> displayGetter,
            Func<object, string> sortKeyGetter,
            IReadOnlyList<Func<object, string>> mergeKeyGetters)
        {
            Header = header;
            Width = width;
            SourceObjectName = sourceObjectName;
            SourceMemberName = sourceMemberName;
            _recordsAccessor = recordsAccessor;
            _displayGetter = displayGetter;
            _sortKeyGetter = sortKeyGetter;
            _mergeKeyGetters = mergeKeyGetters;
        }

        public string Header { get; set; }
        public string Description { get; set; } = string.Empty;
        public string ForegroundColor { get; set; } = string.Empty;
        public string BackgroundColor { get; set; } = string.Empty;
        public double Width { get; }
        public bool Fill { get; set; }
        public bool IsHtml => false;
        public TextAlignment ContentAlignment { get; } = TextAlignment.Left;
        public IGriddoCellEditor Editor => GriddoCellEditors.Text;
        public string SourceMemberName { get; }
        public string SourceObjectName { get; }

        public object? GetValue(object recordSource)
        {
            return _displayGetter(recordSource);
        }

        public bool TrySetValue(object recordSource, object? value)
        {
            _ = recordSource;
            _ = value;
            return false;
        }

        public string FormatValue(object? value) => value?.ToString() ?? string.Empty;

        public object? GetSortValue(object recordSource) => _sortKeyGetter(recordSource);

        public bool IsMergedWithPreviousRecord(IReadOnlyList<object> records, int recordIndex)
        {
            if (recordIndex <= 0 || recordIndex >= records.Count)
            {
                return false;
            }

            return AreInSameMergeGroup(records[recordIndex], records[recordIndex - 1]);
        }

        public bool IsMergedWithNextRecord(IReadOnlyList<object> records, int recordIndex)
        {
            if (recordIndex < 0 || recordIndex >= records.Count - 1)
            {
                return false;
            }

            return AreInSameMergeGroup(records[recordIndex], records[recordIndex + 1]);
        }

        private bool AreInSameMergeGroup(object? leftRecord, object? rightRecord)
        {
            if (leftRecord is null || rightRecord is null)
            {
                return false;
            }

            foreach (var getter in _mergeKeyGetters)
            {
                if (!string.Equals(getter(leftRecord), getter(rightRecord), StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private static void ApplyPlotLayout(IPlotFieldLayoutTarget target, PlotFieldDialogResult settings)
    {
        target.TitleSelection = settings.TitleSelection ?? string.Empty;
        target.Label = settings.Label ?? string.Empty;
        target.ShowXAxis = settings.ShowXAxis;
        target.ShowYAxis = settings.ShowYAxis;
        target.XAxisTitle = settings.XAxisTitle ?? string.Empty;
        target.YAxisTitle = settings.YAxisTitle ?? string.Empty;
        target.XAxisUnit = settings.XAxisUnit ?? string.Empty;
        target.YAxisUnit = settings.YAxisUnit ?? string.Empty;
        target.XAxisLabelPrecision = Math.Clamp(settings.XAxisLabelPrecision, 0, 10);
        target.YAxisLabelPrecision = Math.Clamp(settings.YAxisLabelPrecision, 0, 10);
    }

    private static void ApplyHtmlLayout(IHtmlFieldLayoutTarget target, HtmlFieldConfiguration settings)
    {
        target.FontFamilyName = settings.FontFamilyName ?? string.Empty;
        target.FontSize = Math.Max(0, settings.FontSize);
        target.FontStyleName = settings.FontStyleName ?? string.Empty;
        target.Segments = settings.Segments
            .Select(s => new HtmlFieldSegmentConfiguration
            {
                SourceFieldIndex = s.SourceFieldIndex,
                Enabled = s.Enabled,
                AbbreviatedHeaderOverride = s.AbbreviatedHeaderOverride ?? string.Empty,
                AddLineBreakAfter = s.AddLineBreakAfter,
                WordWrap = s.WordWrap
            })
            .ToList();
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

public sealed class PrimaryDemoSource
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ResultFlagKey { get; set; } = string.Empty;
    public string ResultFlagDisplay { get; set; } = string.Empty;
    public string ReviewStatusKey { get; set; } = string.Empty;
    public string ReviewStatusDisplay { get; set; } = string.Empty;
    public bool Active { get; set; }
    public string HtmlSnippet { get; set; } = string.Empty;
    public Geometry Graphic { get; set; } = Geometry.Empty;
}

public sealed class AnalyticsDemoSource
{
    public double Score { get; set; }
    public int PlottoSeed { get; set; }
}
