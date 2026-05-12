using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;


namespace Griddo.Grid;

public readonly record struct GriddoSortDescriptor(int FieldIndex, bool Ascending, int Priority);

public sealed partial class Griddo

{
    /// <summary>
    /// Applies sorting for one or more columns. Indices are ordered left-to-right as successive sort keys (priority 1, 2, …).
    /// When <paramref name="additive"/> is false, replaces the current sort. When true, appends columns that are not yet in the sort list
    /// after existing keys (Ctrl when invoking the command, or Ctrl when opening the header menu).
    /// </summary>
    public void ApplyFieldHeaderSort(IReadOnlyList<int> fieldIndices, bool ascending, bool additive)

    {
        var ordered = fieldIndices
            .Where(i => i >= 0 && i < Fields.Count)
            .Distinct()
            .OrderBy(i => i)
            .ToList();

        if (ordered.Count == 0) return;


        if (!additive)

        {
            var descriptors = ordered
                .Select((fieldIndex, level) => new GriddoSortDescriptor(fieldIndex, ascending, level + 1))
                .ToList();

            SetSortDescriptors(descriptors);

            return;
        }


        var existing = SortDescriptors.OrderBy(d => d.Priority).ToList();

        var existingFields = existing.Select(d => d.FieldIndex).ToHashSet();

        var nextPriority = existing.Count == 0 ? 1 : existing.Max(d => d.Priority) + 1;

        foreach (var fieldIndex in ordered)

        {
            if (!existingFields.Add(fieldIndex)) continue;


            existing.Add(new GriddoSortDescriptor(fieldIndex, ascending, nextPriority));

            nextPriority++;
        }


        SetSortDescriptors(existing);
    }


    public void SetSortDescriptors(IEnumerable<GriddoSortDescriptor> descriptors)

    {
        _sortDescriptors.Clear();

        foreach (var d in descriptors
                     .Where(static x => x.FieldIndex >= 0)
                     .OrderBy(static x => x.Priority)
                     .ThenBy(static x => x.FieldIndex))

        {
            if (_sortDescriptors.Any(x => x.FieldIndex == d.FieldIndex)) continue;


            _sortDescriptors.Add(new GriddoSortDescriptor(d.FieldIndex, d.Ascending, _sortDescriptors.Count + 1));
        }


        ApplySorting();

        SortDescriptorsChanged?.Invoke(this, EventArgs.Empty);

        InvalidateVisual();
    }


    private void ToggleHeaderSort(int fieldIndex, bool additive)

    {
        if (fieldIndex < 0 || fieldIndex >= Fields.Count) return;


        if (!additive)

        {
            if (_sortDescriptors.Count == 1 && _sortDescriptors[0].FieldIndex == fieldIndex)

            {
                var current = _sortDescriptors[0];

                _sortDescriptors[0] = current with { Ascending = !current.Ascending, Priority = 1 };
            }

            else

            {
                _sortDescriptors.Clear();

                _sortDescriptors.Add(new GriddoSortDescriptor(fieldIndex, true, 1));
            }
        }

        else

        {
            var idx = _sortDescriptors.FindIndex(x => x.FieldIndex == fieldIndex);

            if (idx >= 0)

            {
                var current = _sortDescriptors[idx];

                _sortDescriptors[idx] = current with { Ascending = !current.Ascending };
            }

            else

            {
                _sortDescriptors.Add(new GriddoSortDescriptor(fieldIndex, true,
                    _sortDescriptors.Count + 1));
            }
        }


        NormalizeSortPriorities();

        ApplySorting();

        SortDescriptorsChanged?.Invoke(this, EventArgs.Empty);

        InvalidateVisual();
    }


    private void NormalizeSortPriorities()

    {
        for (var i = 0; i < _sortDescriptors.Count; i++)

        {
            var d = _sortDescriptors[i];

            _sortDescriptors[i] = d with { Priority = i + 1 };
        }
    }


    private void ApplySorting()

    {
        if (Records.Count <= 1 || _sortDescriptors.Count == 0) return;


        var active = _sortDescriptors
            .Where(d => d.FieldIndex >= 0 && d.FieldIndex < Fields.Count)
            .OrderBy(d => d.Priority)
            .ToList();

        if (active.Count == 0) return;


        var recordCount = Records.Count;

        var keyFields = active.Select(d => Fields[d.FieldIndex]).ToArray();

        var keyValues = new object?[active.Count][];

        for (var k = 0; k < active.Count; k++)

        {
            var keys = new object?[recordCount];

            var col = keyFields[k];

            for (var i = 0; i < recordCount; i++)

                try

                {
                    var recordSource = Records[i];

                    keys[i] = col is Fields.IGriddoFieldSortValueView sortableField
                        ? sortableField.GetSortValue(recordSource)
                        : col.GetValue(recordSource);
                }

                catch

                {
                    keys[i] = null;
                }


            keyValues[k] = keys;
        }


        var sortedOldIndices = Enumerable.Range(0, recordCount).ToList();

        sortedOldIndices.Sort((a, b) =>

        {
            for (var k = 0; k < active.Count; k++)

            {
                var d = active[k];

                var av = keyValues[k][a];

                var bv = keyValues[k][b];

                var cmp = CompareCellValues(av, bv);

                if (cmp != 0) return d.Ascending ? cmp : -cmp;
            }


            return a.CompareTo(b);
        });


        var isAlreadySorted = true;

        for (var i = 0; i < recordCount; i++)

        {
            if (sortedOldIndices[i] == i) continue;


            isAlreadySorted = false;

            break;
        }


        if (isAlreadySorted) return;


        var oldToNew = new int[recordCount];

        for (var newIndex = 0; newIndex < recordCount; newIndex++) oldToNew[sortedOldIndices[newIndex]] = newIndex;


        var sortedRecords = new object[recordCount];

        for (var i = 0; i < recordCount; i++) sortedRecords[i] = Records[sortedOldIndices[i]];


        _suspendGridCollectionChanged++;

        try

        {
            Records.Clear();

            for (var i = 0; i < sortedRecords.Length; i++) Records.Add(sortedRecords[i]);
        }

        finally

        {
            _suspendGridCollectionChanged--;
        }


        RemapSelectionAfterRecordMove(oldToNew);

        OnGridCollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }


    private static int CompareCellValues(object? a, object? b)

    {
        if (a is null && b is null) return 0;


        if (a is null) return -1;


        if (b is null) return 1;


        if (a.GetType() == b.GetType() && a is IComparable sameTypeComparable) return sameTypeComparable.CompareTo(b);


        if (a is IComparable comparableA)

            try

            {
                return comparableA.CompareTo(b);
            }

            catch

            {
                // Fall through to string compare.
            }


        var sa = Convert.ToString(a, CultureInfo.CurrentCulture) ?? string.Empty;

        var sb = Convert.ToString(b, CultureInfo.CurrentCulture) ?? string.Empty;

        return StringComparer.CurrentCultureIgnoreCase.Compare(sa, sb);
    }


    private int TryGetSortPriorityForField(int fieldIndex, out bool ascending)

    {
        for (var i = 0; i < _sortDescriptors.Count; i++)

        {
            var d = _sortDescriptors[i];

            if (d.FieldIndex != fieldIndex) continue;


            ascending = d.Ascending;

            return d.Priority;
        }


        ascending = true;

        return 0;
    }


    private void RemapSortDescriptorsAfterFieldMove(int[] oldToNew)

    {
        if (oldToNew.Length == 0 || _sortDescriptors.Count == 0) return;


        for (var i = 0; i < _sortDescriptors.Count; i++)

        {
            var d = _sortDescriptors[i];

            if (d.FieldIndex >= 0 && d.FieldIndex < oldToNew.Length)
                _sortDescriptors[i] = d with { FieldIndex = oldToNew[d.FieldIndex] };
        }
    }
}