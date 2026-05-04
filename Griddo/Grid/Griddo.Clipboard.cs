using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using Griddo.Clipboard;
using Griddo.Columns;
using Griddo.Primitives;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    public void CopyToClipboard()
    {
        if (_isEditing)
        {
            CopyEditBufferToClipboard();
            return;
        }

        CopySelectedCellsToClipboard();
    }

    public void PasteFromClipboard()
    {
        if (_isEditing)
        {
            PasteClipboardIntoEditBuffer();
            return;
        }

        PasteClipboardIntoGrid();
    }

    public void CutToClipboard()
    {
        if (_isEditing)
        {
            CutEditBufferToClipboard();
            return;
        }

        CutSelectedCellsToClipboard();
    }

    public void ClearCells()
    {
        if (_isEditing)
        {
            return;
        }

        ClearSelectedCells();
    }

    private void CutSelectedCellsToClipboard()
    {
        CopySelectedCellsToClipboard();
        ClearSelectedCells();
    }

    private void ClearSelectedCells()
    {
        if (Rows.Count == 0 || Columns.Count == 0)
        {
            return;
        }

        var targetCells = _selectedCells.Count > 0
            ? _selectedCells.ToList()
            : [_currentCell];
        var didChange = false;
        foreach (var address in targetCells)
        {
            if (address.RowIndex < 0 || address.RowIndex >= Rows.Count || address.ColumnIndex < 0 || address.ColumnIndex >= Columns.Count)
            {
                continue;
            }

            var column = Columns[address.ColumnIndex];
            var row = Rows[address.RowIndex];
            if (column.Editor.TryCommit(string.Empty, out var emptyValue) && column.TrySetValue(row, emptyValue))
            {
                didChange = true;
                continue;
            }

            if (column.TrySetValue(row, null))
            {
                didChange = true;
            }
        }

        if (didChange)
        {
            InvalidateVisual();
        }
    }

    private void CopySelectedCellsToClipboard()
    {
        if (_selectedCells.Count == 0)
        {
            return;
        }

        var minRow = _selectedCells.Min(c => c.RowIndex);
        var maxRow = _selectedCells.Max(c => c.RowIndex);
        var minCol = _selectedCells.Min(c => c.ColumnIndex);
        var maxCol = _selectedCells.Max(c => c.ColumnIndex);

        var lines = new List<string>();
        for (var row = minRow; row <= maxRow; row++)
        {
            var values = new List<string>();
            for (var col = minCol; col <= maxCol; col++)
            {
                var address = new GriddoCellAddress(row, col);
                if (!_selectedCells.Contains(address))
                {
                    values.Add(string.Empty);
                    continue;
                }

                var value = Columns[col].FormatValue(Columns[col].GetValue(Rows[row]));
                values.Add(value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '));
            }

            lines.Add(string.Join('\t', values));
        }

        var tableHtml = new StringBuilder();
        tableHtml.Append(
            "<table border=\"1\" cellspacing=\"0\" cellpadding=\"4\" style=\"border-collapse:collapse;font-family:Segoe UI,sans-serif;font-size:")
            .Append(EffectiveFontSize.ToString(CultureInfo.InvariantCulture))
            .Append("px\">");

        for (var row = minRow; row <= maxRow; row++)
        {
            tableHtml.Append("<tr>");
            for (var col = minCol; col <= maxCol; col++)
            {
                var address = new GriddoCellAddress(row, col);
                if (!_selectedCells.Contains(address))
                {
                    tableHtml.Append("<td></td>");
                    continue;
                }

                var column = Columns[col];
                var cellRect = GetCellRect(row, col);
                var cw = Math.Max(1, (int)Math.Round(cellRect.Width));
                var ch = Math.Max(1, (int)Math.Round(cellRect.Height));

                var flat = column.FormatValue(column.GetValue(Rows[row]))
                    .Replace('\t', ' ')
                    .Replace('\r', ' ')
                    .Replace('\n', ' ');

                if (column is IGriddoHostedColumnView hosted
                    && hosted.TryGetClipboardHtmlFragment(
                        TryGetHostedElement(address),
                        Rows[row],
                        cw,
                        ch,
                        out var fragment))
                {
                    tableHtml.Append("<td style=\"vertical-align:middle\">").Append(fragment).Append("</td>");
                }
                else if (column.IsHtml)
                {
                    var (pngBytes, pw, ph) = GriddoValuePainter.RenderHtmlCellToPng(
                        column.GetValue(Rows[row]),
                        cw,
                        ch,
                        column.ContentAlignment,
                        EffectiveFontSize);
                    var b64 = Convert.ToBase64String(pngBytes);
                    tableHtml.Append("<td style=\"vertical-align:middle\"><img src=\"data:image/png;base64,")
                        .Append(b64)
                        .Append("\" alt=\"\" width=\"")
                        .Append(pw)
                        .Append("\" height=\"")
                        .Append(ph)
                        .Append("\" /></td>");
                }
                else
                {
                    tableHtml.Append("<td style=\"vertical-align:middle\">")
                        .Append(GriddoClipboardHtml.EscapeCellText(flat))
                        .Append("</td>");
                }
            }

            tableHtml.Append("</tr>");
        }

        tableHtml.Append("</table>");

        var tsv = string.Join(Environment.NewLine, lines);
        var cfHtml = GriddoClipboardHtml.EncodeHtmlFragment(tableHtml.ToString());

        var dataObject = new DataObject();
        dataObject.SetText(tsv, TextDataFormat.UnicodeText);
        dataObject.SetText(tsv, TextDataFormat.Text);

        // CF_HTML offsets are UTF-8 byte counts; pass UTF-8 bytes so Excel gets valid HTML Format (string path can mis-encode).
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var htmlBytes = utf8.GetBytes(cfHtml);
        dataObject.SetData(DataFormats.Html, new MemoryStream(htmlBytes, writable: false));

        System.Windows.Clipboard.SetDataObject(dataObject, copy: true);
    }

    private void PasteClipboardIntoGrid()
    {
        if (!System.Windows.Clipboard.ContainsText())
        {
            return;
        }

        var startCell = _selectedCells.Count > 0
            ? new GriddoCellAddress(
                _selectedCells.Min(c => c.RowIndex),
                _selectedCells.Min(c => c.ColumnIndex))
            : _currentCell;
        if (startCell.RowIndex < 0 || startCell.ColumnIndex < 0)
        {
            return;
        }

        var text = System.Windows.Clipboard.GetText();
        var rowChunks = text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.None);

        for (var rowOffset = 0; rowOffset < rowChunks.Length; rowOffset++)
        {
            var targetRow = startCell.RowIndex + rowOffset;
            if (targetRow < 0 || targetRow >= Rows.Count)
            {
                break;
            }

            var cells = rowChunks[rowOffset].Split('\t');
            for (var colOffset = 0; colOffset < cells.Length; colOffset++)
            {
                var targetCol = startCell.ColumnIndex + colOffset;
                if (targetCol < 0 || targetCol >= Columns.Count)
                {
                    break;
                }

                var column = Columns[targetCol];
                if (column.Editor.TryCommit(cells[colOffset], out var parsed))
                {
                    column.TrySetValue(Rows[targetRow], parsed);
                }
            }
        }

        InvalidateVisual();
    }
}
