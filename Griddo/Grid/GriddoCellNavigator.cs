using System.Windows.Input;
using Griddo.Primitives;

namespace Griddo.Grid;

internal static class GriddoCellNavigator
{
    public static bool TryGetTarget(
        Key key,
        bool isCtrlPressed,
        GriddoCellAddress currentCell,
        int recordCount,
        int fieldCount,
        out GriddoCellAddress target)
    {
        target = currentCell;
        if (recordCount == 0 || fieldCount == 0)
        {
            return false;
        }

        switch (key)
        {
            case Key.Left:
                target = new GriddoCellAddress(
                    currentCell.RecordIndex,
                    isCtrlPressed ? 0 : Math.Max(0, currentCell.FieldIndex - 1));
                return true;
            case Key.Right:
                target = new GriddoCellAddress(
                    currentCell.RecordIndex,
                    isCtrlPressed ? fieldCount - 1 : Math.Min(fieldCount - 1, currentCell.FieldIndex + 1));
                return true;
            case Key.Up:
                target = new GriddoCellAddress(
                    isCtrlPressed ? 0 : Math.Max(0, currentCell.RecordIndex - 1),
                    currentCell.FieldIndex);
                return true;
            case Key.Down:
                target = new GriddoCellAddress(
                    isCtrlPressed ? recordCount - 1 : Math.Min(recordCount - 1, currentCell.RecordIndex + 1),
                    currentCell.FieldIndex);
                return true;
            case Key.Home:
                target = isCtrlPressed
                    ? new GriddoCellAddress(0, 0)
                    : new GriddoCellAddress(currentCell.RecordIndex, 0);
                return true;
            case Key.End:
                target = isCtrlPressed
                    ? new GriddoCellAddress(recordCount - 1, fieldCount - 1)
                    : new GriddoCellAddress(currentCell.RecordIndex, fieldCount - 1);
                return true;
            default:
                return false;
        }
    }
}
