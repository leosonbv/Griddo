using System.Windows;
using Griddo.Grid;
using Griddo.Primitives;

namespace Griddo.Columns;

public sealed class GriddoCellContextMenuEventArgs : EventArgs
{
    public GriddoCellContextMenuEventArgs(GriddoCellAddress cell, Point positionInGrid, bool cellWasAlreadySelected)
    {
        Cell = cell;
        PositionInGrid = positionInGrid;
        CellWasAlreadySelected = cellWasAlreadySelected;
    }

    public GriddoCellAddress Cell { get; }

    public Point PositionInGrid { get; }

    public bool CellWasAlreadySelected { get; }

    /// <summary>Set to true to suppress opening <see cref="Griddo.CellContextMenu"/>.</summary>
    public bool Handled { get; set; }
}
