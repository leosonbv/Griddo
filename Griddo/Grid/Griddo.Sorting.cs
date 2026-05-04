using System.Collections;
using System.Collections.Specialized;
using System.Globalization;

namespace Griddo.Grid;

public readonly record struct GriddoSortDescriptor(int ColumnIndex, bool Ascending, int Priority);

public sealed partial class Griddo
{
    public void SetSortDescriptors(IEnumerable<GriddoSortDescriptor> descriptors)
    {
        _sortDescriptors.Clear();
        foreach (var d in descriptors
                     .Where(static x => x.ColumnIndex >= 0)
                     .OrderBy(static x => x.Priority)
                     .ThenBy(static x => x.ColumnIndex))
        {
            if (_sortDescriptors.Any(x => x.ColumnIndex == d.ColumnIndex))
            {
                continue;
            }

            _sortDescriptors.Add(new GriddoSortDescriptor(d.ColumnIndex, d.Ascending, _sortDescriptors.Count + 1));
        }

        ApplySorting();
        InvalidateVisual();
    }

    private void ToggleHeaderSort(int columnIndex, bool additive)
    {
        if (columnIndex < 0 || columnIndex >= Columns.Count)
        {
            return;
        }

        if (!additive)
        {
            if (_sortDescriptors.Count == 1 && _sortDescriptors[0].ColumnIndex == columnIndex)
            {
                var current = _sortDescriptors[0];
                _sortDescriptors[0] = current with { Ascending = !current.Ascending, Priority = 1 };
            }
            else
            {
                _sortDescriptors.Clear();
                _sortDescriptors.Add(new GriddoSortDescriptor(columnIndex, Ascending: true, Priority: 1));
            }
        }
        else
        {
            var idx = _sortDescriptors.FindIndex(x => x.ColumnIndex == columnIndex);
            if (idx >= 0)
            {
                var current = _sortDescriptors[idx];
                _sortDescriptors[idx] = current with { Ascending = !current.Ascending };
            }
            else
            {
                _sortDescriptors.Add(new GriddoSortDescriptor(columnIndex, Ascending: true, Priority: _sortDescriptors.Count + 1));
            }
        }

        NormalizeSortPriorities();
        ApplySorting();
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
        if (Rows.Count <= 1 || _sortDescriptors.Count == 0)
        {
            return;
        }

        var active = _sortDescriptors
            .Where(d => d.ColumnIndex >= 0 && d.ColumnIndex < Columns.Count)
            .OrderBy(d => d.Priority)
            .ToList();
        if (active.Count == 0)
        {
            return;
        }

        var rowCount = Rows.Count;
        var keyColumns = active.Select(d => Columns[d.ColumnIndex]).ToArray();
        var keyValues = new object?[active.Count][];
        for (var k = 0; k < active.Count; k++)
        {
            var keys = new object?[rowCount];
            var col = keyColumns[k];
            for (var i = 0; i < rowCount; i++)
            {
                try
                {
                    keys[i] = col.GetValue(Rows[i]);
                }
                catch
                {
                    keys[i] = null;
                }
            }

            keyValues[k] = keys;
        }

        var sortedOldIndices = Enumerable.Range(0, rowCount).ToList();
        sortedOldIndices.Sort((a, b) =>
        {
            for (var k = 0; k < active.Count; k++)
            {
                var d = active[k];
                var av = keyValues[k][a];
                var bv = keyValues[k][b];
                var cmp = CompareCellValues(av, bv);
                if (cmp != 0)
                {
                    return d.Ascending ? cmp : -cmp;
                }
            }

            return a.CompareTo(b);
        });

        var isAlreadySorted = true;
        for (var i = 0; i < rowCount; i++)
        {
            if (sortedOldIndices[i] == i)
            {
                continue;
            }

            isAlreadySorted = false;
            break;
        }

        if (isAlreadySorted)
        {
            return;
        }

        var sortedRows = new object[rowCount];
        for (var i = 0; i < rowCount; i++)
        {
            sortedRows[i] = Rows[sortedOldIndices[i]];
        }

        _suspendGridCollectionChanged++;
        try
        {
            Rows.Clear();
            for (var i = 0; i < sortedRows.Length; i++)
            {
                Rows.Add(sortedRows[i]);
            }
        }
        finally
        {
            _suspendGridCollectionChanged--;
        }

        OnGridCollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));

        _selectedCells.Clear();
        if (Rows.Count > 0 && Columns.Count > 0)
        {
            _currentCell = new Primitives.GriddoCellAddress(Math.Clamp(_currentCell.RowIndex, 0, Rows.Count - 1), Math.Clamp(_currentCell.ColumnIndex, 0, Columns.Count - 1));
            _selectedCells.Add(_currentCell);
        }
    }

    private static int CompareCellValues(object? a, object? b)
    {
        if (a is null && b is null)
        {
            return 0;
        }

        if (a is null)
        {
            return -1;
        }

        if (b is null)
        {
            return 1;
        }

        if (a.GetType() == b.GetType() && a is IComparable sameTypeComparable)
        {
            return sameTypeComparable.CompareTo(b);
        }

        if (a is IComparable comparableA)
        {
            try
            {
                return comparableA.CompareTo(b);
            }
            catch
            {
                // Fall through to string compare.
            }
        }

        var sa = Convert.ToString(a, CultureInfo.CurrentCulture) ?? string.Empty;
        var sb = Convert.ToString(b, CultureInfo.CurrentCulture) ?? string.Empty;
        return StringComparer.CurrentCultureIgnoreCase.Compare(sa, sb);
    }

    private int TryGetSortPriorityForColumn(int columnIndex, out bool ascending)
    {
        for (var i = 0; i < _sortDescriptors.Count; i++)
        {
            var d = _sortDescriptors[i];
            if (d.ColumnIndex != columnIndex)
            {
                continue;
            }

            ascending = d.Ascending;
            return d.Priority;
        }

        ascending = true;
        return 0;
    }

    private void RemapSortDescriptorsAfterColumnMove(int[] oldToNew)
    {
        if (oldToNew.Length == 0 || _sortDescriptors.Count == 0)
        {
            return;
        }

        for (var i = 0; i < _sortDescriptors.Count; i++)
        {
            var d = _sortDescriptors[i];
            if (d.ColumnIndex >= 0 && d.ColumnIndex < oldToNew.Length)
            {
                _sortDescriptors[i] = d with { ColumnIndex = oldToNew[d.ColumnIndex] };
            }
        }
    }
}
