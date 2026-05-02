using System;

namespace Griddo;

public sealed class GriddoColumnHeaderMouseEventArgs : EventArgs
{
    public GriddoColumnHeaderMouseEventArgs(int columnIndex)
    {
        ColumnIndex = columnIndex;
    }

    public int ColumnIndex { get; }
}
