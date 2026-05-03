namespace Griddo.Columns;

public sealed class GriddoRowHeaderMouseEventArgs : EventArgs
{
    public GriddoRowHeaderMouseEventArgs(int rowIndex, IReadOnlyList<int>? selectedRowIndices = null)
    {
        RowIndex = rowIndex;
        SelectedRowIndices = selectedRowIndices ?? [rowIndex];
    }

    /// <summary>Row header that was clicked.</summary>
    public int RowIndex { get; }

    /// <summary>All row headers in the active right-click / context scope (e.g. every selected row when opening the menu on one of them).</summary>
    public IReadOnlyList<int> SelectedRowIndices { get; }
}
