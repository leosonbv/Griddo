namespace Griddo.Columns;

public sealed class GriddoColumnHeaderMouseEventArgs : EventArgs
{
    public GriddoColumnHeaderMouseEventArgs(int columnIndex, IReadOnlyList<int>? selectedColumnIndices = null)
    {
        ColumnIndex = columnIndex;
        SelectedColumnIndices = selectedColumnIndices ?? [columnIndex];
    }

    /// <summary>Column header that was clicked.</summary>
    public int ColumnIndex { get; }

    /// <summary>All column headers in the active right-click / context scope (e.g. every selected column when opening the menu on one of them).</summary>
    public IReadOnlyList<int> SelectedColumnIndices { get; }
}
