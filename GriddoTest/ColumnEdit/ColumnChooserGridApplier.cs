using System.Linq;
using Griddo.Columns;
using Griddo.Grid;

namespace GriddoTest.ColumnEdit;

/// <summary>Applies <see cref="ColumnEditDialog"/> results to a live <see cref="Griddo"/>.</summary>
public static class ColumnChooserGridApplier
{
    /// <param name="columnRegistry">When non-null, snapshot and <see cref="ColumnEditRow.SourceColumnIndex"/> refer to this list (includes hidden columns). When null, uses current <see cref="Griddo.Columns"/> only.</param>
    public static void Apply(
        global::Griddo.Grid.Griddo grid,
        IReadOnlyList<ColumnEditRow> orderedRows,
        int frozenColumns,
        int frozenRows,
        IReadOnlyList<IGriddoColumnView>? columnRegistry = null)
    {
        try
        {
            var snap = columnRegistry is { Count: > 0 }
                ? columnRegistry.ToList()
                : grid.Columns.ToList();

            if (snap.Count == 0)
            {
                ApplyFrozenOnly(grid, frozenColumns, frozenRows);
                return;
            }

            var visible = orderedRows.Where(static r => r.Visible).ToList();
            if (visible.Count == 0)
            {
                ApplyFrozenOnly(grid, frozenColumns, frozenRows);
                return;
            }

            var snapCount = snap.Count;
            var canReorder = visible.All(r => r.SourceColumnIndex >= 0 && r.SourceColumnIndex < snapCount);
            if (!canReorder)
            {
                ApplyFrozenOnly(grid, frozenColumns, frozenRows);
                return;
            }

            grid.ClearColumnWidthOverrides();

            grid.Columns.Clear();
            foreach (var r in visible)
            {
                if (r.SourceColumnIndex < 0 || r.SourceColumnIndex >= snap.Count)
                {
                    continue;
                }

                var c = snap[r.SourceColumnIndex];
                c.Fill = r.Fill;
                if (!string.IsNullOrWhiteSpace(r.Title))
                {
                    c.Header = r.Title.Trim();
                }

                grid.Columns.Add(c);
            }

            if (grid.Columns.Count == 0)
            {
                foreach (var c in snap)
                {
                    grid.Columns.Add(c);
                }

                ApplyFrozenOnly(grid, frozenColumns, frozenRows);
                return;
            }

            for (var i = 0; i < grid.Columns.Count && i < visible.Count; i++)
            {
                grid.SetLogicalColumnWidth(i, visible[i].Width);
            }

            ApplyFrozenOnly(grid, frozenColumns, frozenRows);
        }
        finally
        {
            grid.RefreshHostedCells();
        }
    }

    private static void ApplyFrozenOnly(global::Griddo.Grid.Griddo grid, int frozenColumns, int frozenRows)
    {
        grid.FixedColumnCount = Math.Clamp(frozenColumns, 0, grid.Columns.Count);
        grid.FixedRowCount = Math.Clamp(frozenRows, 0, grid.Rows.Count);
        grid.InvalidateMeasure();
        grid.InvalidateVisual();
    }
}
