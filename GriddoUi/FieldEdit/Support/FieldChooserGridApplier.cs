using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Griddo.Fields;
using Griddo.Grid;
using GriddoUi.FieldEdit.Dialog;
using GriddoUi.FieldEdit.Models;

namespace GriddoUi.FieldEdit.Support;

/// <summary>Applies <see cref="FieldConfigurator"/> results to a live <see cref="Griddo"/>.</summary>
public static class FieldChooserGridApplier
{
    /// <param name="fieldRegistry">When non-null, snapshot and <see cref="FieldEditRecord.SourceFieldIndex"/> refer to this list (includes hidden fields). When null, uses current <see cref="Griddo.Fields"/> only.</param>
    /// <param name="persistedLayoutSourceFieldIndices">Source field indices that had persisted layout; initial auto-width is skipped for those fields after apply.</param>
    public static void Apply(
        global::Griddo.Grid.Griddo grid,
        IReadOnlyList<FieldEditRecord> orderedRecords,
        int frozenFields,
        int frozenRecords,
        FieldChooserGeneralOptions? generalOptions = null,
        IReadOnlyList<IGriddoFieldView>? fieldRegistry = null,
        IReadOnlyCollection<int>? persistedLayoutSourceFieldIndices = null)
    {
        try
        {
            var snap = fieldRegistry is { Count: > 0 }
                ? fieldRegistry.ToList()
                : grid.Fields.ToList();

            if (snap.Count == 0)
            {
                grid.SetSortDescriptors([]);
                ApplyFrozenOnly(grid, frozenFields, frozenRecords, generalOptions);
                return;
            }

            var visible = orderedRecords.Where(static r => r.Visible).ToList();
            if (visible.Count == 0)
            {
                grid.SetSortDescriptors([]);
                ApplyFrozenOnly(grid, frozenFields, frozenRecords, generalOptions);
                return;
            }

            var snapCount = snap.Count;
            var canReorder = visible.All(r => r.SourceFieldIndex >= 0 && r.SourceFieldIndex < snapCount);
            if (!canReorder)
            {
                grid.SetSortDescriptors([]);
                ApplyFrozenOnly(grid, frozenFields, frozenRecords, generalOptions);
                return;
            }

            grid.ClearFieldWidthOverrides();
            grid.ClearInitialAutoWidthSuppressions();

            grid.Fields.Clear();
            var sourceToGridIndex = new Dictionary<int, int>();
            foreach (var r in visible)
            {
                if (r.SourceFieldIndex < 0 || r.SourceFieldIndex >= snap.Count)
                {
                    continue;
                }

                var c = snap[r.SourceFieldIndex];
                c.FieldFill = NormalizeFieldFill(r.FieldFill);
                if (!string.IsNullOrWhiteSpace(r.Title))
                {
                    c.Header = r.Title.Trim();
                }
                else
                {
                    c.Header = string.Empty;
                }
                if (c is IGriddoFieldTitleView titleView)
                {
                    titleView.AbbreviatedHeader = r.AbbreviatedTitle?.Trim() ?? string.Empty;
                }

                if (c is IGriddoFieldDescriptionView descriptionView)
                {
                    descriptionView.Description = r.Description?.Trim() ?? string.Empty;
                }

                if (c is IGriddoFieldFormatView formatView)
                {
                    formatView.FormatString = r.FormatString?.Trim() ?? string.Empty;
                }

                if (c is IGriddoFieldFontView fontView)
                {
                    fontView.FontFamilyName = r.FontFamilyName?.Trim() ?? string.Empty;
                    fontView.FontSize = Math.Max(0, r.FontSize);
                    fontView.FontStyleName = r.FontStyleName?.Trim() ?? string.Empty;
                }
                if (c is IGriddoFieldWrapView wrapView)
                {
                    wrapView.NoWrap = r.NoWrap;
                }

                if (c is IGriddoFieldColorView colorView)
                {
                    colorView.ForegroundColor = r.ForegroundColor?.Trim() ?? string.Empty;
                    colorView.BackgroundColor = r.BackgroundColor?.Trim() ?? string.Empty;
                }

                if (c is IGriddoFieldAlignmentView alignmentView)
                {
                    alignmentView.ContentAlignment = FieldMetadataBuilder.EnsureDefaultContentAlignment(
                        r.ContentAlignment,
                        c,
                        r.SampleValue,
                        r.SampleRecordSource?.GetType());
                }

                grid.Fields.Add(c);
                sourceToGridIndex[r.SourceFieldIndex] = grid.Fields.Count - 1;
            }

            if (grid.Fields.Count == 0)
            {
                foreach (var c in snap)
                {
                    grid.Fields.Add(c);
                }

                grid.SetSortDescriptors([]);
                ApplyFrozenOnly(grid, frozenFields, frozenRecords, generalOptions);
                return;
            }

            for (var i = 0; i < grid.Fields.Count && i < visible.Count; i++)
            {
                grid.SetLogicalFieldWidth(i, visible[i].Width);
                if (persistedLayoutSourceFieldIndices is not null
                    && persistedLayoutSourceFieldIndices.Contains(visible[i].SourceFieldIndex))
                {
                    grid.MarkInitialAutoWidthSuppressedForGridField(i);
                }
            }

            var sortDescriptors = orderedRecords
                .Where(r => r.SortPriority > 0)
                .OrderBy(r => r.SortPriority)
                .ThenBy(r => r.SourceFieldIndex)
                .Select(r =>
                {
                    if (TryResolveSortGridColumnIndex(r.SourceFieldIndex, sourceToGridIndex, snap, grid, out var colIndex))
                    {
                        return (ok: true, d: new GriddoSortDescriptor(colIndex, r.SortAscending, r.SortPriority));
                    }

                    return (ok: false, d: default(GriddoSortDescriptor));
                })
                .Where(x => x.ok)
                .Select(x => x.d)
                .ToList();

            grid.SetSortDescriptors(sortDescriptors);

            ApplyFrozenOnly(grid, frozenFields, frozenRecords, generalOptions);
        }
        finally
        {
            grid.RefreshHostedCells();
        }
    }

    /// <summary>
    /// Maps persisted catalog index to current grid column index. Prefer the layout apply map; fall back to
    /// locating the same <see cref="IGriddoFieldView"/> instance in <paramref name="grid"/>.Fields (stable across reorder).
    /// </summary>
    private static int NormalizeFieldFill(int fieldFill) =>
        fieldFill <= 0 ? 0 : Math.Min(fieldFill, 3);

    private static bool TryResolveSortGridColumnIndex(
        int sourceFieldIndex,
        IReadOnlyDictionary<int, int> sourceToGridIndex,
        IReadOnlyList<IGriddoFieldView> snap,
        global::Griddo.Grid.Griddo grid,
        out int gridColumnIndex)
    {
        gridColumnIndex = -1;
        if (sourceToGridIndex.TryGetValue(sourceFieldIndex, out var mapped))
        {
            gridColumnIndex = mapped;
            return true;
        }

        if (sourceFieldIndex < 0 || sourceFieldIndex >= snap.Count)
        {
            return false;
        }

        var fieldView = snap[sourceFieldIndex];
        for (var gi = 0; gi < grid.Fields.Count; gi++)
        {
            if (ReferenceEquals(grid.Fields[gi], fieldView))
            {
                gridColumnIndex = gi;
                return true;
            }
        }

        return false;
    }

    private static void ApplyFrozenOnly(
        global::Griddo.Grid.Griddo grid,
        int frozenFields,
        int frozenRecords,
        FieldChooserGeneralOptions? generalOptions)
    {
        grid.FixedFieldCount = Math.Clamp(frozenFields, 0, grid.Fields.Count);
        grid.FixedRecordCount = Math.Clamp(frozenRecords, 0, grid.Records.Count);
        if (generalOptions is not null)
        {
            var minRecordThickness = global::Griddo.Grid.Griddo.GetDefaultMinimumRecordThickness();
            grid.UniformRecordHeight = Math.Max(minRecordThickness, generalOptions.RecordThickness);
            grid.VisibleRecordCount = generalOptions.VisibleRecordCount;
            grid.ShowCellSelectionColoring = generalOptions.ShowSelectionColor;
            grid.HideSelectionWhenGridLosesFocus = generalOptions.HideSelectionWhenNoFocus;
            grid.ShowCurrentCellColor = generalOptions.ShowCurrentCellRect;
            grid.ShowRecordHeaderSelectionColoring = generalOptions.ShowRecordSelectionColor;
            grid.ShowFieldHeaderSelectionColoring = generalOptions.ShowColSelectionColor;
            grid.ShowEditCellColor = generalOptions.ShowEditCellRect;
            grid.ShowSortingIndicators = generalOptions.ShowSortingIndicators;
            grid.ShowHorizontalScrollBar = generalOptions.ShowHorizontalScrollBar;
            grid.ShowVerticalScrollBar = generalOptions.ShowVerticalScrollBar;
            grid.IsTransposed = generalOptions.IsTransposed;
            // "Immediate edit" is Plotto/hosted-fields only.
            grid.ImmediateCellEditOnSingleClick = false;
            grid.HostedPlotDirectEditOnMouseDown = generalOptions.ImmediatePlottoEdit;
        }

        grid.InvalidateMeasure();
        grid.InvalidateVisual();
    }
}
