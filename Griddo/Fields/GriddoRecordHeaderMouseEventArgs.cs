namespace Griddo.Fields;

public sealed class GriddoRecordHeaderMouseEventArgs : EventArgs
{
    public GriddoRecordHeaderMouseEventArgs(int recordIndex, IReadOnlyList<int>? selectedRecordIndices = null)
    {
        RecordIndex = recordIndex;
        SelectedRecordIndices = selectedRecordIndices ?? [recordIndex];
    }

    /// <summary>Record header that was clicked.</summary>
    public int RecordIndex { get; }

    /// <summary>All record headers in the active right-click / context scope (e.g. every selected record when opening the menu on one of them).</summary>
    public IReadOnlyList<int> SelectedRecordIndices { get; }
}
