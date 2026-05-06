using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Griddo.Fields;

public static class GriddoValuePainter
{
    public static void Paint(
        DrawingContext drawingContext,
        object? value,
        Rect bounds,
        Typeface typeface,
        double fontSize,
        Brush foregroundBrush,
        bool underline = false,
        bool treatAsHtml = false,
        bool autoDetectHtml = true,
        TextAlignment alignment = TextAlignment.Left,
        VerticalAlignment verticalAlignment = VerticalAlignment.Top,
        bool noWrap = false)
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
            DrawGeometryInBounds(drawingContext, geometry, bounds, foregroundBrush);
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
                DrawHtmlTable(drawingContext, text, bounds, typeface, fontSize, foregroundBrush, verticalAlignment, noWrap);
                return;
            }

            DrawHtmlText(drawingContext, text, bounds, typeface, fontSize, foregroundBrush, alignment, verticalAlignment, noWrap);
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
        if (underline)
        {
            formatted.SetTextDecorations(TextDecorations.Underline);
        }

        formatted.TextAlignment = alignment;
        formatted.MaxTextWidth = Math.Max(1, bounds.Width - 8);
        formatted.MaxTextHeight = Math.Max(1, bounds.Height - 4);
        formatted.MaxLineCount = noWrap ? 1 : int.MaxValue;
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        var topPadding = 2.0;
        var y = verticalAlignment == VerticalAlignment.Center
            ? bounds.Y + Math.Max(0, (bounds.Height - formatted.Height) / 2)
            : bounds.Y + topPadding;
        drawingContext.DrawText(formatted, new Point(bounds.X + 4, y));
    }

    /// <summary>Renders a simple checkbox centered in <paramref name="bounds"/> (DIP).</summary>
    public static void DrawBoolCheckbox(DrawingContext drawingContext, bool isChecked, Rect bounds, double fontSize)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var side = Math.Clamp(fontSize * 0.9, 11.0, 18.0);
        var x = bounds.X + Math.Max(0, (bounds.Width - side) / 2);
        var y = bounds.Y + Math.Max(0, (bounds.Height - side) / 2);
        var box = new Rect(x, y, side, side);

        var border = new Pen(new SolidColorBrush(Color.FromRgb(96, 96, 96)), 1);
        drawingContext.DrawRectangle(Brushes.White, border, box);

        if (!isChecked)
        {
            return;
        }

        var stroke = new Pen(Brushes.Black, Math.Max(1.2, side * 0.11))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        var m = side * 0.22;
        var ix0 = box.Left + m;
        var iy0 = box.Top + side * 0.52;
        var ix1 = box.Left + side * 0.38;
        var iy1 = box.Bottom - m;
        var ix2 = box.Right - m * 0.85;
        var iy2 = box.Top + side * 0.28;
        drawingContext.DrawLine(stroke, new Point(ix0, iy0), new Point(ix1, iy1));
        drawingContext.DrawLine(stroke, new Point(ix1, iy1), new Point(ix2, iy2));
    }

    /// <summary>Places geometry in world space so it scales uniformly and centers inside <paramref name="bounds"/>.</summary>
    private static void DrawGeometryInBounds(DrawingContext drawingContext, Geometry geometry, Rect bounds, Brush foregroundBrush)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var gb = geometry.Bounds;
        if (gb.IsEmpty || gb.Width <= 0 || gb.Height <= 0)
        {
            return;
        }

        const double pad = 4.0;
        var inner = new Rect(
            bounds.X + pad,
            bounds.Y + pad,
            Math.Max(0, bounds.Width - pad * 2),
            Math.Max(0, bounds.Height - pad * 2));
        if (inner.Width <= 0 || inner.Height <= 0)
        {
            return;
        }

        var scale = Math.Min(inner.Width / gb.Width, inner.Height / gb.Height);
        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
        {
            return;
        }

        var group = new TransformGroup();
        group.Children.Add(new TranslateTransform(-gb.Left, -gb.Top));
        group.Children.Add(new ScaleTransform(scale, scale));
        group.Children.Add(new TranslateTransform(
            inner.X + (inner.Width - gb.Width * scale) / 2,
            inner.Y + (inner.Height - gb.Height * scale) / 2));

        drawingContext.PushTransform(group);
        drawingContext.DrawGeometry(foregroundBrush, null, geometry);
        drawingContext.Pop();
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
                underline: false,
                treatAsHtml: true,
                autoDetectHtml: true,
                alignment,
                VerticalAlignment.Center);
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

    /// <summary>Vertical counterpart to <see cref="MeasureRenderedWidth"/> (transpose field auto-size, etc.).</summary>
    public static double MeasureRenderedHeight(
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
            return imageSource.Height;
        }

        if (value is Geometry geometry)
        {
            return geometry.Bounds.Height;
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

        return formatted.Height;
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
        TextAlignment alignment,
        VerticalAlignment verticalAlignment = VerticalAlignment.Center,
        bool noWrap = false)
    {
        var formatted = BuildHtmlFormattedText(html, typeface, fontSize, foregroundBrush);
        if (formatted.Text.Length == 0)
        {
            return;
        }

        if (TryParseBackgroundColorFromHtml(html, out var backgroundBrush))
        {
            drawingContext.DrawRectangle(backgroundBrush, null, bounds);
        }

        formatted.TextAlignment = alignment;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        const double padX = 4.0;
        const double padY = 2.0;
        // Fill cell width for wrapping; do not force full cell height—use natural height, then cap if needed.
        formatted.MaxTextWidth = Math.Max(1, bounds.Width - padX * 2);
        formatted.MaxLineCount = noWrap ? 1 : int.MaxValue;

        var innerH = Math.Max(1, bounds.Height - padY * 2);
        if (formatted.Height > innerH)
        {
            formatted.MaxTextHeight = innerH;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }

        double y;
        switch (verticalAlignment)
        {
            case VerticalAlignment.Top:
                y = bounds.Y + padY;
                break;
            case VerticalAlignment.Bottom:
                y = bounds.Bottom - padY - formatted.Height;
                break;
            default:
                y = bounds.Y + padY + Math.Max(0, (innerH - formatted.Height) / 2.0);
                break;
        }

        drawingContext.DrawText(formatted, new Point(bounds.X + padX, y));
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
        Brush foregroundBrush,
        VerticalAlignment verticalAlignment,
        bool noWrap = false)
    {
        var records = ParseHtmlTable(html);
        if (records.Count == 0)
        {
            DrawHtmlText(drawingContext, html, bounds, typeface, fontSize, foregroundBrush, TextAlignment.Left, VerticalAlignment.Center);
            return;
        }

        var fieldCount = records.Max(r => r.Count);
        if (fieldCount <= 0)
        {
            return;
        }

        const double tableMargin = 5.0;
        const double cellPadX = 2.0;
        const double cellPadY = 1.0;
        const double minColWidth = 28.0;
        var cellFont = Math.Max(4, fontSize - 1);

        var availW = Math.Max(0, bounds.Width - tableMargin * 2);
        var availH = Math.Max(0, bounds.Height - tableMargin * 2);

        var colWidths = new double[fieldCount];
        for (var col = 0; col < fieldCount; col++)
        {
            var maxIntrinsic = 0.0;
            for (var record = 0; record < records.Count; record++)
            {
                var cellText = col < records[record].Count ? records[record][col] : string.Empty;
                var ft = LooksLikeHtml(cellText)
                    ? BuildHtmlFormattedText(cellText, typeface, cellFont, foregroundBrush)
                    : new FormattedText(
                        cellText,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        cellFont,
                        foregroundBrush,
                        1.0);
                maxIntrinsic = Math.Max(maxIntrinsic, ft.WidthIncludingTrailingWhitespace);
            }

            colWidths[col] = Math.Max(minColWidth, maxIntrinsic + cellPadX * 2);
        }

        var naturalTableWidth = colWidths.Sum();
        if (naturalTableWidth > availW && naturalTableWidth > 0)
        {
            var sx = availW / naturalTableWidth;
            for (var c = 0; c < fieldCount; c++)
            {
                colWidths[c] *= sx;
            }

        }

        var tableLeft = bounds.X + tableMargin;
        var maxTableHeight = availH;

        var recordHeights = new double[records.Count];
        for (var record = 0; record < records.Count; record++)
        {
            var maxContentH = 0.0;
            for (var col = 0; col < fieldCount; col++)
            {
                var cellText = col < records[record].Count ? records[record][col] : string.Empty;
                var formatted = LooksLikeHtml(cellText)
                    ? BuildHtmlFormattedText(cellText, typeface, cellFont, foregroundBrush)
                    : new FormattedText(
                        cellText,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        cellFont,
                        foregroundBrush,
                        1.0);
                formatted.MaxTextWidth = Math.Max(1, colWidths[col] - cellPadX * 2);
                formatted.MaxLineCount = noWrap ? 1 : int.MaxValue;
                maxContentH = Math.Max(maxContentH, formatted.Height);
            }

            recordHeights[record] = maxContentH + cellPadY * 2;
        }

        var naturalTotalHeight = recordHeights.Sum();
        var yStart = bounds.Y + tableMargin;
        if (naturalTotalHeight <= maxTableHeight)
        {
            var slack = maxTableHeight - naturalTotalHeight;
            switch (verticalAlignment)
            {
                case VerticalAlignment.Bottom:
                    yStart += slack;
                    break;
                case VerticalAlignment.Top:
                    break;
                default:
                    yStart += slack / 2.0;
                    break;
            }
        }
        else
        {
            var scale = maxTableHeight / naturalTotalHeight;
            for (var i = 0; i < recordHeights.Length; i++)
            {
                recordHeights[i] *= scale;
            }
        }

        var currentY = yStart;
        for (var record = 0; record < records.Count; record++)
        {
            var recordH = recordHeights[record];
            var maxCellTextH = Math.Max(1, recordH - cellPadY * 2);
            var cellX = tableLeft;
            for (var col = 0; col < fieldCount; col++)
            {
                var cw = colWidths[col];
                var cellRect = new Rect(cellX, currentY, cw, recordH);

                var cellText = col < records[record].Count ? records[record][col] : string.Empty;
                var innerRect = new Rect(
                    cellRect.X + cellPadX,
                    cellRect.Y + cellPadY,
                    Math.Max(1, cw - cellPadX * 2),
                    Math.Max(1, maxCellTextH));

                if (TryParseBackgroundColorFromHtml(cellText, out var backgroundBrush))
                {
                    drawingContext.DrawRectangle(backgroundBrush, null, innerRect);
                }

                if (LooksLikeHtml(cellText))
                {
                    var formatted = BuildHtmlFormattedText(cellText, typeface, cellFont, foregroundBrush);
                    formatted.MaxTextWidth = Math.Max(1, innerRect.Width);
                    formatted.MaxTextHeight = Math.Max(1, innerRect.Height);
                    formatted.MaxLineCount = noWrap ? 1 : int.MaxValue;
                    formatted.Trimming = TextTrimming.CharacterEllipsis;
                    drawingContext.DrawText(formatted, new Point(innerRect.X, innerRect.Y));
                }
                else
                {
                    var formatted = new FormattedText(
                        cellText,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        cellFont,
                        foregroundBrush,
                        1.0);
                    formatted.MaxTextWidth = Math.Max(1, innerRect.Width);
                    formatted.MaxTextHeight = Math.Max(1, innerRect.Height);
                    formatted.MaxLineCount = noWrap ? 1 : int.MaxValue;
                    formatted.Trimming = TextTrimming.CharacterEllipsis;
                    drawingContext.DrawText(formatted, new Point(innerRect.X, innerRect.Y));
                }
                cellX += cw;
            }

            currentY += recordH;
        }
    }

    private static List<List<string>> ParseHtmlTable(string html)
    {
        var records = new List<List<string>>();
        var index = 0;
        while (TryFindTagBlock(html, "tr", index, out var recordStart, out var recordInner, out var recordEnd))
        {
            var cells = new List<string>();
            var cellIndex = 0;
            while (TryFindTagBlock(recordInner, "td", cellIndex, out var _, out var cellInner, out var cellEnd)
                || TryFindTagBlock(recordInner, "th", cellIndex, out var _, out cellInner, out cellEnd))
            {
                cells.Add(cellInner.Trim());
                cellIndex = cellEnd;
            }

            if (cells.Count > 0)
            {
                records.Add(cells);
            }

            index = recordEnd;
        }

        return records;
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

    private static bool TryParseBackgroundColorFromHtml(string html, out Brush brush)
    {
        brush = Brushes.Transparent;
        if (string.IsNullOrWhiteSpace(html))
        {
            return false;
        }

        var marker = "background-color:";
        var idx = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return false;
        }

        var start = idx + marker.Length;
        var end = html.IndexOfAny([';', '"', '\''], start);
        var token = (end > start ? html[start..end] : html[start..]).Trim();
        if (token.Length == 0)
        {
            return false;
        }

        try
        {
            if (ColorConverter.ConvertFromString(token) is Color color)
            {
                brush = new SolidColorBrush(color);
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
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
