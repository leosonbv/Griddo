using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Griddo;
public static class GriddoValuePainter
{
    public static void Paint(
        DrawingContext drawingContext,
        object? value,
        Rect bounds,
        Typeface typeface,
        double fontSize,
        Brush foregroundBrush,
        bool treatAsHtml = false,
        bool autoDetectHtml = true,
        TextAlignment alignment = TextAlignment.Left,
        VerticalAlignment verticalAlignment = VerticalAlignment.Top)
    {
        if (value is IGriddoSizedImageValue sizedImageValue)
        {
            value = sizedImageValue.GetImage(bounds.Size);
        }

        if (value is ImageSource imageSource)
        {
            drawingContext.DrawImage(imageSource, bounds);
            return;
        }

        if (value is Geometry geometry)
        {
            drawingContext.DrawGeometry(foregroundBrush, null, geometry);
            return;
        }

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var text = value?.ToString() ?? string.Empty;
        if (treatAsHtml || (autoDetectHtml && LooksLikeHtml(text)))
        {
            if (LooksLikeHtmlTable(text))
            {
                DrawHtmlTable(drawingContext, text, bounds, typeface, fontSize, foregroundBrush);
                return;
            }

            DrawHtmlText(drawingContext, text, bounds, typeface, fontSize, foregroundBrush, alignment);
            return;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            foregroundBrush,
            1.0);

        formatted.TextAlignment = alignment;
        formatted.MaxTextWidth = Math.Max(1, bounds.Width - 8);
        formatted.MaxTextHeight = Math.Max(1, bounds.Height - 4);
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        var topPadding = 2.0;
        var y = verticalAlignment == VerticalAlignment.Center
            ? bounds.Y + Math.Max(0, (bounds.Height - formatted.Height) / 2)
            : bounds.Y + topPadding;
        drawingContext.DrawText(formatted, new Point(bounds.X + 4, y));
    }

    /// <summary>Rasterize HTML cell content like on-screen paint (white background), for clipboard PNG.</summary>
    public static (byte[] PngBytes, int Width, int Height) RenderHtmlCellToPng(
        object? value,
        int cellWidthPx,
        int cellHeightPx,
        TextAlignment alignment,
        double fontSize = 12)
    {
        var w = Math.Max(1, cellWidthPx);
        var h = Math.Max(1, cellHeightPx);
        const int maxEdge = 480;
        if (w > maxEdge || h > maxEdge)
        {
            var scale = Math.Min((double)maxEdge / w, (double)maxEdge / h);
            w = Math.Max(1, (int)(w * scale));
            h = Math.Max(1, (int)(h * scale));
        }

        var typeface = new Typeface("Segoe UI");
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            Paint(
                dc,
                value,
                new Rect(0, 0, w, h),
                typeface,
                fontSize,
                Brushes.Black,
                treatAsHtml: true,
                autoDetectHtml: true,
                alignment,
                VerticalAlignment.Top);
        }

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return (ms.ToArray(), w, h);
    }

    public static double MeasureRenderedWidth(
        object? value,
        Typeface typeface,
        double fontSize,
        bool treatAsHtml = false)
    {
        if (value is IGriddoSizedImageValue sizedImageValue)
        {
            value = sizedImageValue.GetImage(new Size(120, 24));
        }

        if (value is ImageSource imageSource)
        {
            return imageSource.Width;
        }

        if (value is Geometry geometry)
        {
            return geometry.Bounds.Width;
        }

        var text = value?.ToString() ?? string.Empty;
        var formatted = (treatAsHtml || LooksLikeHtml(text))
            ? BuildHtmlFormattedText(text, typeface, fontSize, Brushes.Black)
            : new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.Black,
                1.0);

        return formatted.WidthIncludingTrailingWhitespace;
    }

    private static bool LooksLikeHtml(string value)
        => value.Contains('<') && value.Contains('>');

    private static bool LooksLikeHtmlTable(string value)
        => value.Contains("<table", StringComparison.OrdinalIgnoreCase);

    private static string StripTags(string input)
    {
        var chars = new List<char>(input.Length);
        var insideTag = false;

        foreach (var ch in input)
        {
            if (ch == '<')
            {
                insideTag = true;
                continue;
            }

            if (ch == '>')
            {
                insideTag = false;
                continue;
            }

            if (!insideTag)
            {
                chars.Add(ch);
            }
        }

        return new string(chars.ToArray()).Trim();
    }

    private static void DrawHtmlText(
        DrawingContext drawingContext,
        string html,
        Rect bounds,
        Typeface typeface,
        double fontSize,
        Brush foregroundBrush,
        TextAlignment alignment)
    {
        var formatted = BuildHtmlFormattedText(html, typeface, fontSize, foregroundBrush);
        if (formatted.Text.Length == 0)
        {
            return;
        }

        formatted.TextAlignment = alignment;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        formatted.MaxTextWidth = Math.Max(1, bounds.Width - 8);
        formatted.MaxTextHeight = Math.Max(1, bounds.Height - 4);
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        drawingContext.DrawText(formatted, new Point(bounds.X + 4, bounds.Y + 2));
    }

    private static FormattedText BuildHtmlFormattedText(string html, Typeface typeface, double fontSize, Brush foregroundBrush)
    {
        var runs = ParseHtmlRuns(html);
        var plainTextBuilder = new StringBuilder();
        foreach (var run in runs)
        {
            plainTextBuilder.Append(run.Text);
        }

        var plainText = plainTextBuilder.ToString();
        var formatted = new FormattedText(
            plainText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            foregroundBrush,
            1.0);

        var offset = 0;
        foreach (var run in runs)
        {
            if (run.Text.Length == 0)
            {
                continue;
            }

            formatted.SetFontWeight(run.Bold ? FontWeights.Bold : FontWeights.Normal, offset, run.Text.Length);
            formatted.SetFontStyle(run.Italic ? FontStyles.Italic : FontStyles.Normal, offset, run.Text.Length);
            if (run.Underline)
            {
                formatted.SetTextDecorations(TextDecorations.Underline, offset, run.Text.Length);
            }

            if (run.Foreground is not null)
            {
                formatted.SetForegroundBrush(run.Foreground, offset, run.Text.Length);
            }

            offset += run.Text.Length;
        }

        return formatted;
    }

    private static List<HtmlRun> ParseHtmlRuns(string html)
    {
        var runs = new List<HtmlRun>();
        var styleStack = new Stack<HtmlStyle>();
        styleStack.Push(new HtmlStyle(false, false, false, null));
        var listStack = new Stack<ListState>();
        var lastOutputEndsWithNewline = true;

        var plain = new StringBuilder();
        var i = 0;
        while (i < html.Length)
        {
            if (html[i] != '<')
            {
                plain.Append(html[i]);
                i++;
                continue;
            }

            var close = html.IndexOf('>', i + 1);
            if (close < 0)
            {
                plain.Append(html[i]);
                i++;
                continue;
            }

            FlushRun();

            var token = html[(i + 1)..close].Trim();
            i = close + 1;
            if (token.Length == 0)
            {
                continue;
            }

            var isClosing = token.StartsWith('/');
            var tag = isClosing ? token[1..].Trim().ToLowerInvariant() : token.ToLowerInvariant();

            if (isClosing)
            {
                if (tag.StartsWith("li"))
                {
                    EnsureLineBreak();
                    continue;
                }

                if (tag.StartsWith("ul") || tag.StartsWith("ol"))
                {
                    if (listStack.Count > 0)
                    {
                        listStack.Pop();
                    }

                    EnsureLineBreak();
                    continue;
                }

                if (styleStack.Count > 1)
                {
                    styleStack.Pop();
                }

                continue;
            }

            if (tag.StartsWith("br"))
            {
                plain.Append('\n');
                continue;
            }

            if (tag.StartsWith("ul"))
            {
                listStack.Push(new ListState(ListKind.Unordered));
                EnsureLineBreak();
                continue;
            }

            if (tag.StartsWith("ol"))
            {
                listStack.Push(new ListState(ListKind.Ordered));
                EnsureLineBreak();
                continue;
            }

            if (tag.StartsWith("li"))
            {
                EnsureLineBreak();
                AppendListPrefix();
                continue;
            }

            var current = styleStack.Peek();
            if (tag.StartsWith("b") || tag.StartsWith("strong"))
            {
                styleStack.Push(current with { Bold = true });
            }
            else if (tag.StartsWith("i") || tag.StartsWith("em"))
            {
                styleStack.Push(current with { Italic = true });
            }
            else if (tag.StartsWith("u"))
            {
                styleStack.Push(current with { Underline = true });
            }
            else if (tag.StartsWith("font"))
            {
                styleStack.Push(current with { Foreground = ParseColor(token) ?? current.Foreground });
            }
        }

        FlushRun();
        return runs;

        void FlushRun()
        {
            if (plain.Length == 0)
            {
                return;
            }

            var s = styleStack.Peek();
            var text = DecodeHtmlEntities(plain.ToString());
            runs.Add(new HtmlRun(text, s.Bold, s.Italic, s.Underline, s.Foreground));
            lastOutputEndsWithNewline = text.EndsWith('\n');
            plain.Clear();
        }

        void EnsureLineBreak()
        {
            if (plain.Length > 0)
            {
                if (plain[^1] != '\n')
                {
                    plain.Append('\n');
                }

                return;
            }

            if (lastOutputEndsWithNewline)
            {
                return;
            }

            plain.Append('\n');
            lastOutputEndsWithNewline = false;
        }

        void AppendListPrefix()
        {
            if (listStack.Count == 0)
            {
                plain.Append("- ");
                return;
            }

            var depth = listStack.Count;
            for (var level = 1; level < depth; level++)
            {
                plain.Append("  ");
            }

            var currentList = listStack.Peek();
            if (currentList.Kind == ListKind.Ordered)
            {
                currentList.Counter++;
                plain.Append(currentList.Counter);
                plain.Append(". ");
            }
            else
            {
                plain.Append("• ");
            }
        }
    }

    private static Brush? ParseColor(string token)
    {
        const string marker = "color=";
        var idx = token.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return null;
        }

        var valueStart = idx + marker.Length;
        var value = token[valueStart..].Trim();
        if (value.StartsWith("\"") || value.StartsWith("'"))
        {
            var quote = value[0];
            var end = value.IndexOf(quote, 1);
            value = end > 1 ? value[1..end] : value[1..];
        }
        else
        {
            var sep = value.IndexOfAny([' ', '>']);
            if (sep > 0)
            {
                value = value[..sep];
            }
        }

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(value);
            return new SolidColorBrush(color);
        }
        catch
        {
            return null;
        }
    }

    private static string DecodeHtmlEntities(string text)
    {
        return text
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("&lt;", "<", StringComparison.OrdinalIgnoreCase)
            .Replace("&gt;", ">", StringComparison.OrdinalIgnoreCase)
            .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase)
            .Replace("&quot;", "\"", StringComparison.OrdinalIgnoreCase)
            .Replace("&#39;", "'", StringComparison.OrdinalIgnoreCase);
    }

    private static void DrawHtmlTable(
        DrawingContext drawingContext,
        string html,
        Rect bounds,
        Typeface typeface,
        double fontSize,
        Brush foregroundBrush)
    {
        var rows = ParseHtmlTable(html);
        if (rows.Count == 0)
        {
            DrawHtmlText(drawingContext, html, bounds, typeface, fontSize, foregroundBrush, TextAlignment.Left);
            return;
        }

        var columnCount = rows.Max(r => r.Count);
        if (columnCount <= 0)
        {
            return;
        }

        var tableRect = new Rect(bounds.X + 1, bounds.Y + 1, Math.Max(0, bounds.Width - 2), Math.Max(0, bounds.Height - 2));
        var rowHeight = tableRect.Height / rows.Count;
        var colWidth = tableRect.Width / columnCount;
        var lineW = Math.Max(0.5, fontSize / 12.0);
        var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(176, 176, 176)), lineW);

        for (var row = 0; row < rows.Count; row++)
        {
            for (var col = 0; col < columnCount; col++)
            {
                var cellRect = new Rect(
                    tableRect.X + col * colWidth,
                    tableRect.Y + row * rowHeight,
                    colWidth,
                    rowHeight);

                var cellText = col < rows[row].Count ? rows[row][col] : string.Empty;
                var formatted = new FormattedText(
                    cellText,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize - 1,
                    foregroundBrush,
                    1.0);

                formatted.MaxTextWidth = Math.Max(1, cellRect.Width - 4);
                formatted.MaxTextHeight = Math.Max(1, cellRect.Height - 2);
                formatted.Trimming = TextTrimming.CharacterEllipsis;
                drawingContext.DrawText(formatted, new Point(cellRect.X + 2, cellRect.Y + 1));
            }
        }

        // Collapsed borders: draw only internal separators, no outer border.
        for (var col = 1; col < columnCount; col++)
        {
            var x = tableRect.X + col * colWidth;
            drawingContext.DrawLine(borderPen, new Point(x, tableRect.Y), new Point(x, tableRect.Bottom));
        }

        for (var row = 1; row < rows.Count; row++)
        {
            var y = tableRect.Y + row * rowHeight;
            drawingContext.DrawLine(borderPen, new Point(tableRect.X, y), new Point(tableRect.Right, y));
        }
    }

    private static List<List<string>> ParseHtmlTable(string html)
    {
        var rows = new List<List<string>>();
        var index = 0;
        while (TryFindTagBlock(html, "tr", index, out var rowStart, out var rowInner, out var rowEnd))
        {
            var cells = new List<string>();
            var cellIndex = 0;
            while (TryFindTagBlock(rowInner, "td", cellIndex, out var _, out var cellInner, out var cellEnd)
                || TryFindTagBlock(rowInner, "th", cellIndex, out var _, out cellInner, out cellEnd))
            {
                var plain = StripTags(cellInner);
                cells.Add(DecodeHtmlEntities(plain));
                cellIndex = cellEnd;
            }

            if (cells.Count > 0)
            {
                rows.Add(cells);
            }

            index = rowEnd;
        }

        return rows;
    }

    private static bool TryFindTagBlock(string source, string tag, int startIndex, out int tagStart, out string inner, out int endIndex)
    {
        var open = "<" + tag;
        var close = "</" + tag + ">";
        tagStart = source.IndexOf(open, startIndex, StringComparison.OrdinalIgnoreCase);
        if (tagStart < 0)
        {
            inner = string.Empty;
            endIndex = -1;
            return false;
        }

        var openEnd = source.IndexOf('>', tagStart);
        if (openEnd < 0)
        {
            inner = string.Empty;
            endIndex = -1;
            return false;
        }

        var closeStart = source.IndexOf(close, openEnd + 1, StringComparison.OrdinalIgnoreCase);
        if (closeStart < 0)
        {
            inner = string.Empty;
            endIndex = -1;
            return false;
        }

        inner = source[(openEnd + 1)..closeStart];
        endIndex = closeStart + close.Length;
        return true;
    }

    private enum ListKind
    {
        Unordered,
        Ordered
    }

    private sealed class ListState
    {
        public ListState(ListKind kind)
        {
            Kind = kind;
        }

        public ListKind Kind { get; }
        public int Counter { get; set; }
    }

    private readonly record struct HtmlStyle(bool Bold, bool Italic, bool Underline, Brush? Foreground);
    private readonly record struct HtmlRun(string Text, bool Bold, bool Italic, bool Underline, Brush? Foreground);
}
