using System.Windows.Input;

namespace Griddo.Fields;

public sealed class GriddoFieldHeaderMouseEventArgs : EventArgs
{
    public GriddoFieldHeaderMouseEventArgs(
        int fieldIndex,
        IReadOnlyList<int>? selectedFieldIndices = null,
        ModifierKeys openModifiers = ModifierKeys.None)
    {
        FieldIndex = fieldIndex;
        SelectedFieldIndices = selectedFieldIndices ?? [fieldIndex];
        OpenModifiers = openModifiers;
    }

    /// <summary>Field header that was clicked.</summary>
    public int FieldIndex { get; }

    /// <summary>All field headers in the active right-click / context scope (e.g. every selected field when opening the menu on one of them).</summary>
    public IReadOnlyList<int> SelectedFieldIndices { get; }

    /// <summary>Keyboard modifiers held when the header context menu was opened (e.g. Ctrl+right-click to append sort levels).</summary>
    public ModifierKeys OpenModifiers { get; }
}
