using System.Windows.Input;
using Griddo.Primitives;

namespace Griddo.Grid;

internal static class GriddoCellNavigator
{
    public static bool TryGetTarget(
        Key key,
        bool isCtrlPressed,
        GriddoCellAddress currentCell,
        int rowCount,
        int columnCount,
        out GriddoCellAddress target)
    {
        target = currentCell;
        if (rowCount == 0 || columnCount == 0)
        {
            return false;
        }

        switch (key)
        {
            case Key.Left:
                target = new GriddoCellAddress(
                    currentCell.RowIndex,
                    isCtrlPressed ? 0 : Math.Max(0, currentCell.ColumnIndex - 1));
                return true;
            case Key.Right:
                target = new GriddoCellAddress(
                    currentCell.RowIndex,
                    isCtrlPressed ? columnCount - 1 : Math.Min(columnCount - 1, currentCell.ColumnIndex + 1));
                return true;
            case Key.Up:
                target = new GriddoCellAddress(
                    isCtrlPressed ? 0 : Math.Max(0, currentCell.RowIndex - 1),
                    currentCell.ColumnIndex);
                return true;
            case Key.Down:
                target = new GriddoCellAddress(
                    isCtrlPressed ? rowCount - 1 : Math.Min(rowCount - 1, currentCell.RowIndex + 1),
                    currentCell.ColumnIndex);
                return true;
            case Key.Home:
                target = isCtrlPressed
                    ? new GriddoCellAddress(0, 0)
                    : new GriddoCellAddress(currentCell.RowIndex, 0);
                return true;
            case Key.End:
                target = isCtrlPressed
                    ? new GriddoCellAddress(rowCount - 1, columnCount - 1)
                    : new GriddoCellAddress(currentCell.RowIndex, columnCount - 1);
                return true;
            default:
                return false;
        }
    }
}
