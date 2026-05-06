namespace Griddo.Fields;

public sealed class GriddoFieldHeaderMouseEventArgs : EventArgs
{
    public GriddoFieldHeaderMouseEventArgs(int fieldIndex, IReadOnlyList<int>? selectedFieldIndices = null)
    {
        FieldIndex = fieldIndex;
        SelectedFieldIndices = selectedFieldIndices ?? [fieldIndex];
    }

    /// <summary>Field header that was clicked.</summary>
    public int FieldIndex { get; }

    /// <summary>All field headers in the active right-click / context scope (e.g. every selected field when opening the menu on one of them).</summary>
    public IReadOnlyList<int> SelectedFieldIndices { get; }
}
