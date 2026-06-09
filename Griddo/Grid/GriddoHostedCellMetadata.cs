using System.Windows;
using Griddo.Primitives;

namespace Griddo.Grid;

/// <summary>
/// Attached metadata for hosted cell host elements so plot gestures can resolve the owning grid/cell
/// without walking the visual tree (hosted canvases are not always parent-linked during hit-testing).
/// </summary>
public static class GriddoHostedCellMetadata
{
    private static readonly DependencyProperty OwningGridProperty =
        DependencyProperty.RegisterAttached(
            "OwningGrid",
            typeof(Griddo),
            typeof(GriddoHostedCellMetadata),
            new FrameworkPropertyMetadata(null));

    private static readonly DependencyProperty CellAddressProperty =
        DependencyProperty.RegisterAttached(
            "CellAddress",
            typeof(GriddoCellAddress),
            typeof(GriddoHostedCellMetadata),
            new FrameworkPropertyMetadata(default(GriddoCellAddress)));

    public static void Set(FrameworkElement element, Griddo grid, GriddoCellAddress address)
    {
        element.SetValue(OwningGridProperty, grid);
        element.SetValue(CellAddressProperty, address);
    }

    public static void Clear(FrameworkElement element)
    {
        element.ClearValue(OwningGridProperty);
        element.ClearValue(CellAddressProperty);
    }

    public static bool TryGet(FrameworkElement element, out Griddo grid, out GriddoCellAddress address)
    {
        grid = (Griddo)element.GetValue(OwningGridProperty);
        address = (GriddoCellAddress)element.GetValue(CellAddressProperty);
        return grid != null && address.IsValid;
    }
}
