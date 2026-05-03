using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Griddo.Columns;
using Griddo.Primitives;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    /// <summary>Copy the topmost selected cell's value in each column to every other selected cell in that column.</summary>
    /// <returns>True if at least one cell was updated.</returns>
    public bool FillSelectionDown()
    {
        var n = ApplyFillDownOrIncrement(increment: false);
        if (n > 0)
        {
            InvalidateVisual();
        }

        return n > 0;
    }

    /// <summary>
    /// Like <see cref="FillSelectionDown"/> but increments the last integer in each column's formatted top cell for
    /// each lower selected row; zero-pads magnitudes so all values share the same digit width.
    /// </summary>
    /// <returns>True if at least one cell was updated.</returns>
    public bool FillSelectionIncrementalDown()
    {
        var n = ApplyFillDownOrIncrement(increment: true);
        if (n > 0)
        {
            InvalidateVisual();
        }

        return n > 0;
    }

    private bool TryHandleFillDown(KeyEventArgs e, bool isCtrlPressed, bool isHostedEditing)
    {
        if (!isCtrlPressed || e.Key != Key.D || isHostedEditing || _isEditing)
        {
            return false;
        }

        if (!FillSelectionDown())
        {
            return false;
        }

        e.Handled = true;
        return true;
    }

    private bool TryHandleIncrementalDown(KeyEventArgs e, bool isCtrlPressed, bool isHostedEditing)
    {
        if (!isCtrlPressed || e.Key != Key.I || isHostedEditing || _isEditing)
        {
            return false;
        }

        if (!FillSelectionIncrementalDown())
        {
            return false;
        }

        e.Handled = true;
        return true;
    }

    private int ApplyFillDownOrIncrement(bool increment)
    {
        if (_isEditing || IsCurrentHostedCellInEditMode() || Rows.Count == 0 || Columns.Count == 0 || _selectedCells.Count < 2)
        {
            return 0;
        }

        var byCol = new Dictionary<int, List<int>>();
        foreach (var addr in _selectedCells)
        {
            if (!addr.IsValid)
            {
                continue;
            }

            if (addr.RowIndex < 0 || addr.RowIndex >= Rows.Count || addr.ColumnIndex < 0 || addr.ColumnIndex >= Columns.Count)
            {
                continue;
            }

            if (!byCol.TryGetValue(addr.ColumnIndex, out var list))
            {
                list = [];
                byCol[addr.ColumnIndex] = list;
            }

            list.Add(addr.RowIndex);
        }

        var writes = 0;
        foreach (var (col, rowList) in byCol.OrderBy(static kv => kv.Key))
        {
            rowList.Sort();
            if (rowList.Count < 2)
            {
                continue;
            }

            var column = Columns[col];
            if (column is IGriddoHostedColumnView)
            {
                continue;
            }

            if (!increment)
            {
                var sourceRow = rowList[0];
                var value = column.GetValue(Rows[sourceRow]);
                for (var i = 1; i < rowList.Count; i++)
                {
                    if (column.TrySetValue(Rows[rowList[i]], value))
                    {
                        writes++;
                    }
                }
            }
            else
            {
                writes += ApplyIncrementForColumn(column, rowList);
            }
        }

        return writes;
    }

    private int ApplyIncrementForColumn(IGriddoColumnView column, List<int> rowList)
    {
        var sourceRow = rowList[0];
        var formatted = column.FormatValue(column.GetValue(Rows[sourceRow]));
        if (!TryFindLastIntegerSpan(formatted, out var intStart, out var intEnd, out var baseLong))
        {
            return 0;
        }

        var prefix = formatted[..intStart];
        var suffix = formatted[intEnd..];
        var count = rowList.Count;
        var values = new long[count];
        values[0] = baseLong;
        try
        {
            for (var i = 1; i < count; i++)
            {
                checked
                {
                    values[i] = baseLong + i;
                }
            }
        }
        catch (OverflowException)
        {
            return 0;
        }

        var maxMagLen = 0;
        foreach (var v in values)
        {
            var len = MagnitudeDigitCount(v);
            if (len > maxMagLen)
            {
                maxMagLen = len;
            }
        }

        var written = 0;
        for (var i = 0; i < count; i++)
        {
            var intText = FormatIncrementInteger(values[i], maxMagLen);
            var newText = prefix + intText + suffix;
            if (!column.Editor.TryCommit(newText, out var parsed))
            {
                continue;
            }

            if (column.TrySetValue(Rows[rowList[i]], parsed))
            {
                written++;
            }
        }

        return written;
    }

    private static int MagnitudeDigitCount(long v)
    {
        if (v == 0)
        {
            return 1;
        }

        if (v == long.MinValue)
        {
            return "9223372036854775808".Length;
        }

        var a = v < 0 ? (ulong)(-v) : (ulong)v;
        return a.ToString(CultureInfo.InvariantCulture).Length;
    }

    private static string FormatIncrementInteger(long v, int paddedMagLen)
    {
        if (v < 0)
        {
            if (v == long.MinValue)
            {
                return "-" + "9223372036854775808".PadLeft(paddedMagLen, '0');
            }

            var mag = ((ulong)(-v)).ToString(CultureInfo.InvariantCulture).PadLeft(paddedMagLen, '0');
            return "-" + mag;
        }

        return ((ulong)v).ToString(CultureInfo.InvariantCulture).PadLeft(paddedMagLen, '0');
    }

    private static bool TryFindLastIntegerSpan(string s, out int start, out int end, out long value)
    {
        start = 0;
        end = 0;
        value = 0;
        var matches = Regex.Matches(s, @"-?\d+");
        if (matches.Count == 0)
        {
            return false;
        }

        var m = matches[^1];
        start = m.Index;
        end = m.Index + m.Length;
        return long.TryParse(
            m.Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }
}
