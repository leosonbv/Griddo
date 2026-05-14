using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using Griddo.Clipboard;
using Griddo.Fields;
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

    public void CopyToClipboardWithHeaders()
    {
        if (_isEditing)
        {
            CopyEditBufferToClipboard();
            return;
        }

        CopySelectedCellsToClipboard(includeHeaders: true);
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

    public void ExportSelectionToExcel()
    {
        if (_selectedCells.Count == 0)
        {
            return;
        }

        if (!TryBuildClipboardPayload(includeHeaders: true, out var tsv, out _))
        {
            return;
        }

        var tempFile = Path.Combine(
            Path.GetTempPath(),
            $"griddo-export-{DateTime.Now:yyyyMMdd-HHmmss-fff}.tsv");
        File.WriteAllText(tempFile, tsv, Encoding.UTF8);
        Process.Start(new ProcessStartInfo
        {
            FileName = tempFile,
            UseShellExecute = true,
            CreateNoWindow = true,
        });
    }

    private void CutSelectedCellsToClipboard()
    {
        CopySelectedCellsToClipboard();
        ClearSelectedCells();
    }

    private void ClearSelectedCells()
    {
        if (Records.Count == 0 || Fields.Count == 0)
        {
            return;
        }

        var targetCells = _selectedCells.Count > 0
            ? _selectedCells.ToList()
            : [_currentCell];
        var didChange = false;
        foreach (var address in targetCells)
        {
            if (address.RecordIndex < 0 || address.RecordIndex >= Records.Count || address.FieldIndex < 0 || address.FieldIndex >= Fields.Count)
            {
                continue;
            }

            var field = Fields[address.FieldIndex];
            if (!FieldAllowsCellEdit(address.FieldIndex))
            {
                continue;
            }

            var record = Records[address.RecordIndex];
            if (field.Editor.TryCommit(string.Empty, out var emptyValue) && field.TrySetValue(record, emptyValue))
            {
                didChange = true;
                continue;
            }

            if (field.TrySetValue(record, null))
            {
                didChange = true;
            }
        }

        if (didChange)
        {
            InvalidateVisual();
        }
    }

    private void CopySelectedCellsToClipboard(bool includeHeaders = false)
    {
        if (!includeHeaders && TryCopySingleHostedCellImageOnlyToClipboard())
        {
            return;
        }

        if (!TryBuildClipboardPayload(includeHeaders, out var tsv, out var cfHtml))
        {
            return;
        }

        var dataObject = new DataObject();
        dataObject.SetText(tsv, TextDataFormat.UnicodeText);
        dataObject.SetText(tsv, TextDataFormat.Text);

        // CF_HTML offsets are UTF-8 byte counts; pass UTF-8 bytes so Excel gets valid HTML Format (string path can mis-encode).
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var htmlBytes = utf8.GetBytes(cfHtml);
        dataObject.SetData(DataFormats.Html, new MemoryStream(htmlBytes, writable: false));

        System.Windows.Clipboard.SetDataObject(dataObject, copy: true);
    }

    private bool TryCopySingleHostedCellImageOnlyToClipboard()
    {
        if (_selectedCells.Count != 1)
        {
            return false;
        }

        var address = _selectedCells.First();
        if (address.RecordIndex < 0 || address.RecordIndex >= Records.Count || address.FieldIndex < 0 || address.FieldIndex >= Fields.Count)
        {
            return false;
        }

        if (Fields[address.FieldIndex] is not IGriddoHostedFieldView hosted)
        {
            return false;
        }

        var cellRect = GetCellRect(address.RecordIndex, address.FieldIndex);
        var cw = Math.Max(1, (int)Math.Round(cellRect.Width));
        var ch = Math.Max(1, (int)Math.Round(cellRect.Height));
        if (!hosted.TryGetClipboardHtmlFragment(
                TryGetHostedElement(address),
                Records[address.RecordIndex],
                cw,
                ch,
                out var fragment))
        {
            return false;
        }

        var marker = "base64,";
        var markerIndex = fragment.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return false;
        }

        var dataStart = markerIndex + marker.Length;
        var dataEnd = fragment.IndexOf('"', dataStart);
        if (dataEnd <= dataStart)
        {
            return false;
        }

        byte[] pngBytes;
        try
        {
            pngBytes = Convert.FromBase64String(fragment[dataStart..dataEnd]);
        }
        catch
        {
            return false;
        }

        var bitmap = new BitmapImage();
        using (var ms = new MemoryStream(pngBytes, writable: false))
        {
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
        }

        var dataObject = new DataObject();
        dataObject.SetImage(bitmap);
        System.Windows.Clipboard.SetDataObject(dataObject, copy: true);
        return true;
    }

    private bool TryBuildClipboardPayload(bool includeHeaders, out string tsv, out string cfHtml)
    {
        tsv = string.Empty;
        cfHtml = string.Empty;
        if (_selectedCells.Count == 0)
        {
            return false;
        }

        var minRecord = _selectedCells.Min(c => c.RecordIndex);
        var maxRecord = _selectedCells.Max(c => c.RecordIndex);
        var minCol = _selectedCells.Min(c => c.FieldIndex);
        var maxCol = _selectedCells.Max(c => c.FieldIndex);

        var selectedInBounds = new HashSet<GriddoCellAddress>();
        foreach (var c in _selectedCells)
        {
            if (c.RecordIndex >= minRecord && c.RecordIndex <= maxRecord && c.FieldIndex >= minCol && c.FieldIndex <= maxCol)
            {
                selectedInBounds.Add(c);
            }
        }

        var lines = new List<string>();
        var tableHtml = new StringBuilder();
        tableHtml.Append(
            "<table border=\"1\" cellspacing=\"0\" cellpadding=\"4\" style=\"border-collapse:collapse;font-family:Segoe UI,sans-serif;font-size:")
            .Append(EffectiveFontSize.ToString(CultureInfo.InvariantCulture))
            .Append("px\">");

        if (includeHeaders)
        {
            var headerValues = new List<string>();
            tableHtml.Append("<tr>");
            for (var col = minCol; col <= maxCol; col++)
            {
                var header = Fields[col].Header ?? string.Empty;
                var flatHeader = header.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
                headerValues.Add(flatHeader);
                tableHtml.Append("<th style=\"text-align:left\">")
                    .Append(GriddoClipboardHtml.EscapeCellText(flatHeader))
                    .Append("</th>");
            }

            tableHtml.Append("</tr>");
            lines.Add(string.Join('\t', headerValues));
        }

        for (var record = minRecord; record <= maxRecord; record++)
        {
            var values = new List<string>();
            tableHtml.Append("<tr>");
            for (var col = minCol; col <= maxCol; col++)
            {
                var address = new GriddoCellAddress(record, col);
                if (!selectedInBounds.Contains(address))
                {
                    values.Add(string.Empty);
                    tableHtml.Append("<td></td>");
                    continue;
                }

                var field = Fields[col];
                var value = GetClipboardCellText(field, Records[record]);
                var flat = value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
                values.Add(flat);

                var cellRect = GetCellRect(record, col);
                var cw = Math.Max(1, (int)Math.Round(cellRect.Width));
                var ch = Math.Max(1, (int)Math.Round(cellRect.Height));

                if (field is IGriddoHostedFieldView hosted
                    && hosted.TryGetClipboardHtmlFragment(
                        TryGetHostedElement(address),
                        Records[record],
                        cw,
                        ch,
                        out var fragment))
                {
                    tableHtml.Append("<td style=\"vertical-align:middle\">").Append(fragment).Append("</td>");
                }
                else if (field.IsHtml)
                {
                    var (pngBytes, pw, ph) = GriddoValuePainter.RenderHtmlCellToPng(
                        field.GetValue(Records[record]),
                        cw,
                        ch,
                        field.ContentAlignment,
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
            lines.Add(string.Join('\t', values));
        }

        tableHtml.Append("</table>");
        tsv = string.Join(Environment.NewLine, lines);
        cfHtml = GriddoClipboardHtml.EncodeHtmlFragment(tableHtml.ToString());
        return true;
    }

    private static string GetClipboardCellText(IGriddoFieldView field, object recordSource)
    {
        var raw = field.GetValue(recordSource);
        var formatted = field.FormatValue(raw);
        if (!string.IsNullOrEmpty(formatted))
        {
            return formatted;
        }

        if (field is IGriddoFieldSortValueView sortableField)
        {
            return field.FormatValue(sortableField.GetSortValue(recordSource));
        }

        return formatted;
    }

    private void PasteClipboardIntoGrid()
    {
        if (!System.Windows.Clipboard.ContainsText())
        {
            return;
        }

        var startCell = _selectedCells.Count > 0
            ? new GriddoCellAddress(
                _selectedCells.Min(c => c.RecordIndex),
                _selectedCells.Min(c => c.FieldIndex))
            : _currentCell;
        if (startCell.RecordIndex < 0 || startCell.FieldIndex < 0)
        {
            return;
        }

        var text = System.Windows.Clipboard.GetText();
        var recordChunks = text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.None);

        for (var recordOffset = 0; recordOffset < recordChunks.Length; recordOffset++)
        {
            var targetRecord = startCell.RecordIndex + recordOffset;
            if (targetRecord < 0 || targetRecord >= Records.Count)
            {
                break;
            }

            var cells = recordChunks[recordOffset].Split('\t');
            for (var colOffset = 0; colOffset < cells.Length; colOffset++)
            {
                var targetCol = startCell.FieldIndex + colOffset;
                if (targetCol < 0 || targetCol >= Fields.Count)
                {
                    break;
                }

                var field = Fields[targetCol];
                if (!FieldAllowsCellEdit(targetCol))
                {
                    continue;
                }

                if (field.Editor.TryCommit(cells[colOffset], out var parsed))
                {
                    field.TrySetValue(Records[targetRecord], parsed);
                }
            }
        }

        InvalidateVisual();
    }
}
