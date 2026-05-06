using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Griddo.Fields;
using Griddo.Primitives;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    private enum IncrementPaddingMode : byte
    {
        None,
        KeepSourceWidth,
        NormalizeSeries
    }

    /// <summary>Copy the topmost selected cell's value in each field to every other selected cell in that field.</summary>
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
    /// Like <see cref="FillSelectionDown"/> but increments the last integer in each field's formatted top cell for
    /// each lower selected record; zero-pads magnitudes so all values share the same digit width.
    /// </summary>
    /// <returns>True if at least one cell was updated.</returns>
    public bool FillSelectionIncrementalDown()
    {
        var n = ApplyFillDownOrIncrement(increment: true, paddingMode: IncrementPaddingMode.NormalizeSeries);
        if (n > 0)
        {
            InvalidateVisual();
        }

        return n > 0;
    }

    /// <summary>
    /// Like <see cref="FillSelectionDown"/> but increments the last integer in each field's formatted top cell for
    /// each lower selected record and controls whether magnitudes are left-padded with zeros.
    /// </summary>
    /// <param name="zeroPad">When true, magnitudes are left-padded to normalize series width; when false, natural digit width is used.</param>
    /// <returns>True if at least one cell was updated.</returns>
    public bool FillSelectionIncrementalDown(bool zeroPad)
    {
        var paddingMode = zeroPad ? IncrementPaddingMode.NormalizeSeries : IncrementPaddingMode.None;
        var n = ApplyFillDownOrIncrement(increment: true, paddingMode: paddingMode);
        if (n > 0)
        {
            InvalidateVisual();
        }

        return n > 0;
    }

    /// <summary>
    /// Like <see cref="FillSelectionDown"/> but increments the last integer while keeping the source number padding width.
    /// </summary>
    /// <returns>True if at least one cell was updated.</returns>
    public bool FillSelectionIncrementalDownKeepPadding()
    {
        var n = ApplyFillDownOrIncrement(increment: true, paddingMode: IncrementPaddingMode.KeepSourceWidth);
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

        var modifiers = Keyboard.Modifiers;
        var shiftPressed = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        var altPressed = (modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
        var changed = altPressed
            ? FillSelectionIncrementalDownKeepPadding()
            : FillSelectionIncrementalDown(zeroPad: shiftPressed);
        if (!changed)
        {
            return false;
        }

        e.Handled = true;
        return true;
    }

    private int ApplyFillDownOrIncrement(bool increment, IncrementPaddingMode paddingMode = IncrementPaddingMode.NormalizeSeries)
    {
        if (_isEditing || IsCurrentHostedCellInEditMode() || Records.Count == 0 || Fields.Count == 0 || _selectedCells.Count < 2)
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

            if (addr.RecordIndex < 0 || addr.RecordIndex >= Records.Count || addr.FieldIndex < 0 || addr.FieldIndex >= Fields.Count)
            {
                continue;
            }

            if (!byCol.TryGetValue(addr.FieldIndex, out var list))
            {
                list = [];
                byCol[addr.FieldIndex] = list;
            }

            list.Add(addr.RecordIndex);
        }

        var writes = 0;
        foreach (var (col, recordList) in byCol.OrderBy(static kv => kv.Key))
        {
            recordList.Sort();
            if (recordList.Count < 2)
            {
                continue;
            }

            var field = Fields[col];
            if (field is IGriddoHostedFieldView)
            {
                continue;
            }

            if (!increment)
            {
                var sourceRecord = recordList[0];
                var value = field.GetValue(Records[sourceRecord]);
                for (var i = 1; i < recordList.Count; i++)
                {
                    if (field.TrySetValue(Records[recordList[i]], value))
                    {
                        writes++;
                    }
                }
            }
            else
            {
                writes += ApplyIncrementForField(field, recordList, paddingMode);
            }
        }

        return writes;
    }

    private int ApplyIncrementForField(IGriddoFieldView field, List<int> recordList, IncrementPaddingMode paddingMode)
    {
        var sourceRecord = recordList[0];
        var formatted = field.FormatValue(field.GetValue(Records[sourceRecord]));
        if (!TryFindLastIntegerSpan(formatted, out var intStart, out var intEnd, out var baseLong, out var minMagDigitsFromSource))
        {
            return 0;
        }

        var prefix = formatted[..intStart];
        var suffix = formatted[intEnd..];
        var count = recordList.Count;
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
            var intText = FormatIncrementInteger(values[i], maxMagLen, minMagDigitsFromSource, paddingMode);
            var newText = prefix + intText + suffix;
            if (!field.Editor.TryCommit(newText, out var parsed))
            {
                continue;
            }

            if (field.TrySetValue(Records[recordList[i]], parsed))
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

    private static string FormatIncrementInteger(
        long v,
        int paddedMagLen,
        int minMagnitudeDigitsFromSource,
        IncrementPaddingMode paddingMode)
    {
        var magLen = paddingMode switch
        {
            IncrementPaddingMode.NormalizeSeries => Math.Max(paddedMagLen, minMagnitudeDigitsFromSource),
            IncrementPaddingMode.KeepSourceWidth => minMagnitudeDigitsFromSource,
            _ => 0
        };
        var applyPadding = paddingMode != IncrementPaddingMode.None;
        if (v < 0)
        {
            if (v == long.MinValue)
            {
                var minValueText = "9223372036854775808";
                return "-" + (applyPadding ? minValueText.PadLeft(magLen, '0') : minValueText);
            }

            var mag = ((ulong)(-v)).ToString(CultureInfo.InvariantCulture);
            if (applyPadding)
            {
                mag = mag.PadLeft(magLen, '0');
            }

            return "-" + mag;
        }

        var positive = ((ulong)v).ToString(CultureInfo.InvariantCulture);
        return applyPadding ? positive.PadLeft(magLen, '0') : positive;
    }

    private static bool TryFindLastIntegerSpan(
        string s,
        out int start,
        out int end,
        out long value,
        out int minMagnitudeDigitWidth)
    {
        start = 0;
        end = 0;
        value = 0;
        minMagnitudeDigitWidth = 0;
        var matches = Regex.Matches(s, @"-?\d+");
        for (var mi = matches.Count - 1; mi >= 0; mi--)
        {
            var m = matches[mi];
            if (IsDigitMatchEmbeddedInHexColorLiteral(s, m))
            {
                continue;
            }

            var matchStr = m.Value;
            var negative = matchStr.StartsWith('-');
            minMagnitudeDigitWidth = negative ? matchStr.Length - 1 : matchStr.Length;
            start = m.Index;
            end = m.Index + m.Length;
            if (long.TryParse(matchStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Skips digit runs inside <c>#RRGGBB</c> / <c>#RGB</c> literals so increment targets field numbers (e.g. font size), not color channels.</summary>
    private static bool IsDigitMatchEmbeddedInHexColorLiteral(string s, Match m)
    {
        var hashIndex = s.LastIndexOf('#', m.Index);
        if (hashIndex < 0)
        {
            return false;
        }

        var i = hashIndex + 1;
        while (i < s.Length && IsAsciiHexDigit(s[i]))
        {
            i++;
        }

        var hexRunEnd = i;
        return m.Index > hashIndex && m.Index + m.Length <= hexRunEnd;
    }

    private static bool IsAsciiHexDigit(char c) =>
        c is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f';
}
