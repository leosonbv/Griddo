using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    private static readonly Brush GriddoContextMenuSelectionBrush = CreateFrozenBrush(Color.FromRgb(0, 122, 204));
    private static readonly Brush GriddoContextMenuSelectionForegroundBrush = Brushes.White;

    private ContextMenu BuildDefaultBodyCellContextMenu()
    {
        var copyItem = new MenuItem { Header = "Copy", InputGestureText = "Ctrl+C" };
        copyItem.Click += (_, _) => CopyToClipboard();

        var copyHeadersItem = new MenuItem { Header = "Copy with headers", InputGestureText = "Ctrl+Shift+C" };
        copyHeadersItem.Click += (_, _) => CopyToClipboardWithHeaders();

        var cutItem = new MenuItem { Header = "Cut", InputGestureText = "Ctrl+X" };
        cutItem.Click += (_, _) => CutToClipboard();

        var pasteItem = new MenuItem { Header = "Paste", InputGestureText = "Ctrl+V" };
        pasteItem.Click += (_, _) => PasteFromClipboard();

        var clearItem = new MenuItem { Header = "Clear", InputGestureText = "Delete" };
        clearItem.Click += (_, _) => ClearCells();

        var fillDownItem = new MenuItem { Header = "Fill down", InputGestureText = "Ctrl+D" };
        fillDownItem.Click += (_, _) => FillSelectionDown();

        var incrementNoPadItem = new MenuItem { Header = "Increment down (no pad)", InputGestureText = "Ctrl+I" };
        incrementNoPadItem.Click += (_, _) => FillSelectionIncrementalDown(zeroPad: false);

        var incrementPadItem = new MenuItem { Header = "Increment down (zero pad)", InputGestureText = "Ctrl+Shift+I" };
        incrementPadItem.Click += (_, _) => FillSelectionIncrementalDown(zeroPad: true);

        var incrementKeepPadItem = new MenuItem { Header = "Increment down (keep pad)", InputGestureText = "Ctrl+Alt+I" };
        incrementKeepPadItem.Click += (_, _) => FillSelectionIncrementalDownKeepPadding();

        var exportExcelItem = new MenuItem { Header = "Export to Excel", InputGestureText = "Ctrl+E" };
        exportExcelItem.Click += (_, _) => ExportSelectionToExcel();

        var findMenu = new MenuItem { Header = "Find" };
        var findItem = new MenuItem { Header = "Find...", InputGestureText = "Ctrl+F" };
        findItem.Click += (_, _) => OpenFindDialogAndFindFirst();
        var findNextItem = new MenuItem { Header = "Find next", InputGestureText = "F3" };
        findNextItem.Click += (_, _) => FindNext();
        var findPrevItem = new MenuItem { Header = "Find previous", InputGestureText = "Ctrl+F3" };
        findPrevItem.Click += (_, _) => FindPrevious();
        var findCancelItem = new MenuItem { Header = "Cancel find", InputGestureText = "Esc" };
        findCancelItem.Click += (_, _) => CancelFind();
        findMenu.Items.Add(findItem);
        findMenu.Items.Add(findNextItem);
        findMenu.Items.Add(findPrevItem);
        findMenu.Items.Add(new Separator());
        findMenu.Items.Add(findCancelItem);

        var editMenu = new MenuItem { Header = "Edit" };
        var editStartItem = new MenuItem { Header = "Edit cell", InputGestureText = "F2" };
        editStartItem.Click += (_, _) => EditCurrentCell();
        var editExitItem = new MenuItem { Header = "Exit edit", InputGestureText = "Esc" };
        editExitItem.Click += (_, _) => CancelCurrentCellEdit();
        editMenu.Items.Add(editStartItem);
        editMenu.Items.Add(editExitItem);

        var menu = new ContextMenu();
        ApplyGriddoContextMenuSelectionStyle(menu);
        menu.Items.Add(copyItem);
        menu.Items.Add(copyHeadersItem);
        menu.Items.Add(cutItem);
        menu.Items.Add(pasteItem);
        menu.Items.Add(clearItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(fillDownItem);
        menu.Items.Add(incrementNoPadItem);
        menu.Items.Add(incrementPadItem);
        menu.Items.Add(incrementKeepPadItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exportExcelItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(findMenu);
        menu.Items.Add(editMenu);
        return menu;
    }

    private static void ApplyGriddoContextMenuSelectionStyle(ContextMenu menu)
    {
        static ComponentResourceKey MenuItemKey(string resourceId) => new(typeof(MenuItem), resourceId);

        menu.Resources[SystemColors.MenuHighlightBrushKey] = GriddoContextMenuSelectionBrush;
        menu.Resources[SystemColors.HighlightBrushKey] = GriddoContextMenuSelectionBrush;
        menu.Resources["MenuItem.Highlight.Background"] = GriddoContextMenuSelectionBrush;
        menu.Resources["MenuItem.Highlight.Border"] = GriddoContextMenuSelectionBrush;
        menu.Resources["MenuItem.Disabled.Highlight.Background"] = GriddoContextMenuSelectionBrush;
        menu.Resources["MenuItem.Disabled.Highlight.Border"] = GriddoContextMenuSelectionBrush;
        menu.Resources["MenuItem.Selected.Background"] = GriddoContextMenuSelectionBrush;
        menu.Resources["MenuItem.Selected.Border"] = GriddoContextMenuSelectionBrush;
        menu.Resources[MenuItemKey("Highlight.Background")] = GriddoContextMenuSelectionBrush;
        menu.Resources[MenuItemKey("Highlight.Border")] = GriddoContextMenuSelectionBrush;
        menu.Resources[MenuItemKey("Disabled.Highlight.Background")] = GriddoContextMenuSelectionBrush;
        menu.Resources[MenuItemKey("Disabled.Highlight.Border")] = GriddoContextMenuSelectionBrush;
        menu.Resources[MenuItemKey("Selected.Background")] = GriddoContextMenuSelectionBrush;
        menu.Resources[MenuItemKey("Selected.Border")] = GriddoContextMenuSelectionBrush;

        var menuItemStyle = new Style(typeof(MenuItem));
        menuItemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        menuItemStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Black));
        menuItemStyle.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0));

        var highlightedTrigger = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        highlightedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, GriddoContextMenuSelectionBrush));
        highlightedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, GriddoContextMenuSelectionForegroundBrush));
        menuItemStyle.Triggers.Add(highlightedTrigger);

        var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabledTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.DimGray));
        disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0));
        menuItemStyle.Triggers.Add(disabledTrigger);

        var disabledHighlightedTrigger = new MultiTrigger();
        disabledHighlightedTrigger.Conditions.Add(new Condition(UIElement.IsEnabledProperty, false));
        disabledHighlightedTrigger.Conditions.Add(new Condition(MenuItem.IsHighlightedProperty, true));
        disabledHighlightedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, GriddoContextMenuSelectionBrush));
        disabledHighlightedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.DimGray));
        menuItemStyle.Triggers.Add(disabledHighlightedTrigger);

        menu.Resources[typeof(MenuItem)] = menuItemStyle;
    }

    /// <summary>
    /// Bold <c>Sort</c> submenu for field headers: ascending/descending by column order (1, 2, …),
    /// Ctrl while clicking a command or Ctrl when opening the menu appends keys after existing sort.
    /// </summary>
    public MenuItem CreateFieldHeaderSortMenuItem(
        IReadOnlyList<int> selectedFieldIndices,
        ModifierKeys headerOpenModifiers,
        Action? afterSort = null)
    {
        var indices = selectedFieldIndices
            .Where(i => i >= 0 && i < Fields.Count)
            .Distinct()
            .OrderBy(i => i)
            .ToList();

        var sortRoot = new MenuItem
        {
            Header = "Sort",
            FontWeight = FontWeights.Bold,
        };

        void Run(bool ascending)
        {
            if (indices.Count == 0)
            {
                return;
            }

            var additive = (Keyboard.Modifiers & ModifierKeys.Control) != 0
                || (headerOpenModifiers & ModifierKeys.Control) != 0;
            ApplyFieldHeaderSort(indices, ascending, additive);
            afterSort?.Invoke();
        }

        var sortAscItem = new MenuItem
        {
            Header = "Ascending",
            InputGestureText = "Ctrl: add level",
            FontWeight = FontWeights.Normal,
        };
        sortAscItem.Click += (_, _) => Run(ascending: true);

        var sortDescItem = new MenuItem
        {
            Header = "Descending",
            InputGestureText = "Ctrl: add level",
            FontWeight = FontWeights.Normal,
        };
        sortDescItem.Click += (_, _) => Run(ascending: false);

        var sortClearItem = new MenuItem { Header = "Clear sort", FontWeight = FontWeights.Normal };
        sortClearItem.Click += (_, _) =>
        {
            SetSortDescriptors([]);
            afterSort?.Invoke();
        };

        var selectedSet = indices.ToHashSet();
        var sortRemoveSelectedItem = new MenuItem
        {
            Header = "Remove sort (selected fields)",
            FontWeight = FontWeights.Normal,
        };
        sortRemoveSelectedItem.Click += (_, _) =>
        {
            if (selectedSet.Count == 0)
            {
                return;
            }

            var kept = SortDescriptors
                .OrderBy(d => d.Priority)
                .Where(d => !selectedSet.Contains(d.FieldIndex))
                .ToList();
            SetSortDescriptors(kept);
            afterSort?.Invoke();
        };

        sortRoot.Items.Add(sortAscItem);
        sortRoot.Items.Add(sortDescItem);
        sortRoot.Items.Add(new Separator());
        sortRoot.Items.Add(sortClearItem);
        sortRoot.Items.Add(sortRemoveSelectedItem);
        sortRoot.Items.Add(new Separator());
        sortRoot.Items.Add(new MenuItem
        {
            Header = "Tip: Ctrl adds keys after existing sort (or Ctrl when opening menu)",
            IsEnabled = false,
            FontWeight = FontWeights.Normal,
        });

        return sortRoot;
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

