namespace Griddo.Columns;

public sealed class GriddoColumnHeaderMouseEventArgs : EventArgs
{
    public GriddoColumnHeaderMouseEventArgs(int columnIndex)
    {
        ColumnIndex = columnIndex;
    }

    public int ColumnIndex { get; }
}
