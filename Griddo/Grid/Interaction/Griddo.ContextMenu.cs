using System.Windows;
using System.Windows.Controls;

namespace Griddo.Grid;

public sealed partial class Griddo
{
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
}

