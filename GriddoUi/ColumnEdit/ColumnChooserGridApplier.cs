using System.Linq;
using Griddo.Columns;
using Griddo.Grid;

namespace GriddoUi.ColumnEdit;

/// <summary>Applies <see cref="ColumnEditDialog"/> results to a live <see cref="Griddo"/>.</summary>
public static class ColumnChooserGridApplier
{
    /// <param name="columnRegistry">When non-null, snapshot and <see cref="ColumnEditRow.SourceColumnIndex"/> refer to this list (includes hidden columns). When null, uses current <see cref="Griddo.Columns"/> only.</param>
    public static void Apply(
        global::Griddo.Grid.Griddo grid,
        IReadOnlyList<ColumnEditRow> orderedRows,
        int frozenColumns,
        int frozenRows,
        ColumnChooserGeneralOptions? generalOptions = null,
        IReadOnlyList<IGriddoColumnView>? columnRegistry = null)
    {
        try
        {
            var snap = columnRegistry is { Count: > 0 }
                ? columnRegistry.ToList()
                : grid.Columns.ToList();

            if (snap.Count == 0)
            {
                grid.SetSortDescriptors([]);
                ApplyFrozenOnly(grid, frozenColumns, frozenRows, generalOptions);
                return;
            }

            var visible = orderedRows.Where(static r => r.Visible).ToList();
            if (visible.Count == 0)
            {
                grid.SetSortDescriptors([]);
                ApplyFrozenOnly(grid, frozenColumns, frozenRows, generalOptions);
                return;
            }

            var snapCount = snap.Count;
            var canReorder = visible.All(r => r.SourceColumnIndex >= 0 && r.SourceColumnIndex < snapCount);
            if (!canReorder)
            {
                grid.SetSortDescriptors([]);
                ApplyFrozenOnly(grid, frozenColumns, frozenRows, generalOptions);
                return;
            }

            grid.ClearColumnWidthOverrides();

            grid.Columns.Clear();
            var sourceToGridIndex = new Dictionary<int, int>();
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
                else
                {
                    c.Header = string.Empty;
                }
                if (c is IGriddoColumnTitleView titleView)
                {
                    titleView.AbbreviatedHeader = r.AbbreviatedTitle?.Trim() ?? string.Empty;
                }

                if (c is IGriddoColumnFormatView formatView)
                {
                    formatView.FormatString = r.FormatString?.Trim() ?? string.Empty;
                }

                if (c is IGriddoColumnFontView fontView)
                {
                    fontView.FontFamilyName = r.FontFamilyName?.Trim() ?? string.Empty;
                    fontView.FontSize = Math.Max(0, r.FontSize);
                }

                grid.Columns.Add(c);
                sourceToGridIndex[r.SourceColumnIndex] = grid.Columns.Count - 1;
            }

            if (grid.Columns.Count == 0)
            {
                foreach (var c in snap)
                {
                    grid.Columns.Add(c);
                }

                grid.SetSortDescriptors([]);
                ApplyFrozenOnly(grid, frozenColumns, frozenRows, generalOptions);
                return;
            }

            for (var i = 0; i < grid.Columns.Count && i < visible.Count; i++)
            {
                grid.SetLogicalColumnWidth(i, visible[i].Width);
            }

            var sortDescriptors = orderedRows
                .Where(r => r.SortPriority > 0)
                .OrderBy(r => r.SortPriority)
                .ThenBy(r => r.SourceColumnIndex)
                .Select(r =>
                {
                    if (sourceToGridIndex.TryGetValue(r.SourceColumnIndex, out var colIndex))
                    {
                        return (ok: true, d: new GriddoSortDescriptor(colIndex, r.SortAscending, r.SortPriority));
                    }

                    return (ok: false, d: default(GriddoSortDescriptor));
                })
                .Where(x => x.ok)
                .Select(x => x.d)
                .ToList();

            grid.SetSortDescriptors(sortDescriptors);

            ApplyFrozenOnly(grid, frozenColumns, frozenRows, generalOptions);
        }
        finally
        {
            grid.RefreshHostedCells();
        }
    }

    private static void ApplyFrozenOnly(
        global::Griddo.Grid.Griddo grid,
        int frozenColumns,
        int frozenRows,
        ColumnChooserGeneralOptions? generalOptions)
    {
        grid.FixedColumnCount = Math.Clamp(frozenColumns, 0, grid.Columns.Count);
        grid.FixedRowCount = Math.Clamp(frozenRows, 0, grid.Rows.Count);
        if (generalOptions is not null)
        {
            grid.VisibleRowCount = generalOptions.VisibleRowCount;
            grid.ShowCellSelectionColoring = generalOptions.ShowSelectionColor;
            grid.ShowCurrentCellColor = generalOptions.ShowCurrentCellRect;
            grid.ShowRowHeaderSelectionColoring = generalOptions.ShowRowSelectionColor;
            grid.ShowColumnHeaderSelectionColoring = generalOptions.ShowColSelectionColor;
            grid.ShowEditCellColor = generalOptions.ShowEditCellRect;
            grid.ShowSortingIndicators = generalOptions.ShowSortingIndicators;
            // "Immediate edit" is Plotto/hosted-columns only.
            grid.ImmediateCellEditOnSingleClick = false;
            grid.HostedPlotDirectEditOnMouseDown = generalOptions.ImmediatePlottoEdit;
        }

        grid.InvalidateMeasure();
        grid.InvalidateVisual();
    }
}
