using System.Windows;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Plotto.Charting.Axes;
using Plotto.Charting.Core;
using Plotto.Charting.Geometry;
using Plotto.Charting.Rendering;

namespace Plotto.Charting.Controls;

public abstract partial class SkiaChartBaseControl
{
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex HtmlRowRegex = new("<tr[^>]*>(.*?)</tr>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex HtmlCellRegex = new("<td[^>]*>(.*?)</td>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex HtmlStyleRegex = new("style\\s*=\\s*['\\\"](?<style>[^'\\\"]+)['\\\"]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HtmlFontSizeRegex = new("(?<value>[0-9]+(?:\\.[0-9]+)?)\\s*px", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);
        DrawChart(e.Surface.Canvas, e.Info.Width, e.Info.Height);
        _coordinates.MarkPaintLayoutSynced(ActualWidth, ActualHeight);
    }

    protected virtual void DrawSeries(SKCanvas canvas, IReadOnlyList<ChartPoint> points, SKRect plotRect) =>
        ChartSkiaLineSeries.DrawPolyline(canvas, points, plotRect, ToPixelX, ToPixelY, _linePaint);

    protected virtual void DrawOverlay(SKCanvas canvas, SKRect plotRect)
    {
    }

    protected float ToPixelX(double x, SKRect plotRect)
    {
        return plotRect.Left + (float)((x - Viewport.XMin) / (Viewport.XMax - Viewport.XMin) * plotRect.Width);
    }

    protected float ToPixelY(double y, SKRect plotRect)
    {
        return plotRect.Bottom - (float)((y - Viewport.YMin) / (Viewport.YMax - Viewport.YMin) * plotRect.Height);
    }

    private void DrawChart(SKCanvas canvas, int width, int height)
    {
        canvas.Clear(ChartBackgroundColor);

        if (width <= 0 || height <= 0)
        {
            return;
        }

        _coordinates.ApplySurfaceDimensions(
            width,
            height,
            UseSparklineLayout,
            PlotUiScale,
            ShowXAxis,
            ShowYAxis,
            AxisFontSize,
            ShowYAxis && !string.IsNullOrWhiteSpace(AxisLabelY),
            ShowXAxis && !string.IsNullOrWhiteSpace(AxisLabelX));

        if (PlotRect.Width <= 2 || PlotRect.Height <= 2)
        {
            return;
        }

        if (!Viewport.IsValid || Points.Count == 0)
        {
            UpdateViewportFromData();
        }

        var plotRect = PlotRect;
        if (!UseSparklineLayout && ShowChartTitle)
        {
            plotRect = DrawChartTitle(canvas, plotRect);
        }

        canvas.Save();
        canvas.ClipRect(plotRect);
        using (var plotBackgroundPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = false, Color = PlotBackgroundColor })
        {
            canvas.DrawRect(plotRect, plotBackgroundPaint);
        }

        if (Points.Count > 0)
        {
            DrawSeries(canvas, Points, plotRect);
        }

        DrawOverlay(canvas, plotRect);

        canvas.Restore();

        if (!UseSparklineLayout)
        {
            DrawAxes(canvas, plotRect);
        }

        if (_isRightDragZoom && CanInteract())
        {
            var x0 = (float)Math.Min(_zoomRectStart.X, _zoomRectCurrent.X);
            var y0 = (float)Math.Min(_zoomRectStart.Y, _zoomRectCurrent.Y);
            var x1 = (float)Math.Max(_zoomRectStart.X, _zoomRectCurrent.X);
            var y1 = (float)Math.Max(_zoomRectStart.Y, _zoomRectCurrent.Y);
            var rubber = new SKRect(x0, y0, x1, y1);
            canvas.DrawRect(rubber, _zoomRubberFillPaint);
            canvas.DrawRect(rubber, _zoomRubberStrokePaint);
        }
    }

    private SKRect DrawChartTitle(SKCanvas canvas, SKRect plotRect)
    {
        var lines = BuildTitleLines(ChartTitle);
        if (lines.Count == 0)
        {
            return plotRect;
        }

        var defaultFontSize = (float)Math.Max(6d, TitleFontSize) * PlotUiScale;
        var topPadding = 3f * PlotUiScale;
        var bottomPadding = 4f * PlotUiScale;
        var visualRows = BuildVisualRows(lines);
        var rowMetrics = visualRows
            .Select(row => BuildRowMetrics(row, defaultFontSize))
            .ToList();
        var titleBand = topPadding + rowMetrics.Sum(m => m.BeforeGap + m.LineHeight) + bottomPadding;
        if (plotRect.Height <= titleBand + 24f)
        {
            return plotRect;
        }

        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Color = TitleForegroundColor
        };
        using var backgroundPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        var centerX = (plotRect.Left + plotRect.Right) / 2f;
        var baselineY = plotRect.Top + topPadding;
        for (var i = 0; i < visualRows.Count; i++)
        {
            var row = visualRows[i];
            var metrics = rowMetrics[i];
            baselineY += metrics.BeforeGap + metrics.FontSize;

            var segmentLayouts = row
                .Select(line => BuildSegmentLayout(line, metrics.FontSize))
                .ToList();
            var rowWidth = segmentLayouts.Sum(s => s.Width) + ((segmentLayouts.Count - 1) * (12f * PlotUiScale));
            var x = centerX - (rowWidth / 2f);
            foreach (var segment in segmentLayouts)
            {
                if (segment.ValueBackgroundColor.HasValue && !string.IsNullOrWhiteSpace(segment.ValueText))
                {
                    backgroundPaint.Color = segment.ValueBackgroundColor.Value;
                    var rectTop = baselineY - metrics.FontSize - (1f * PlotUiScale);
                    var rectBottom = baselineY + (2f * PlotUiScale);
                    var valueStartX = x + segment.ValueOffsetX;
                    canvas.DrawRect(new SKRect(valueStartX - (2f * PlotUiScale), rectTop, valueStartX + segment.ValueWidth + (2f * PlotUiScale), rectBottom), backgroundPaint);
                }

                if (segment.HasHeader)
                {
                    using var headerFont = new SKFont(ResolveTypeface(segment.HeaderStyle), metrics.FontSize);
                    textPaint.Color = TitleForegroundColor;
                    canvas.DrawText(segment.HeaderText, x, baselineY, SKTextAlign.Left, headerFont, textPaint);
                }

                using var valueFont = new SKFont(ResolveTypeface(segment.ValueStyle), metrics.FontSize);
                textPaint.Color = segment.ValueForegroundColor ?? TitleForegroundColor;
                var valueX = x + segment.ValueOffsetX;
                canvas.DrawText(segment.ValueText, valueX, baselineY, SKTextAlign.Left, valueFont, textPaint);
                x += segment.Width + (12f * PlotUiScale);
            }

            baselineY += metrics.LineHeight - metrics.FontSize;
        }

        return new SKRect(plotRect.Left, plotRect.Top + titleBand, plotRect.Right, plotRect.Bottom);
    }

    private static List<TitleLine> BuildTitleLines(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return [];
        }

        if (title.Contains("<tr", StringComparison.OrdinalIgnoreCase))
        {
            var rows = HtmlRowRegex.Matches(title);
            if (rows.Count > 0)
            {
                var parsed = new List<TitleLine>(rows.Count);
                foreach (Match row in rows)
                {
                    var rowMarkup = row.Value;
                    var rowHtml = row.Groups[1].Value;
                    var breakBefore = rowMarkup.Contains("data-break-before='1'", StringComparison.OrdinalIgnoreCase) ||
                                      rowMarkup.Contains("data-break-before=\"1\"", StringComparison.OrdinalIgnoreCase);
                    var cellMatches = HtmlCellRegex.Matches(rowHtml);
                    if (cellMatches.Count >= 2)
                    {
                        var headerText = CleanHtmlText(cellMatches[0].Groups[1].Value);
                        var valueCellHtml = cellMatches[1].Groups[1].Value;
                        var valueText = CleanHtmlText(valueCellHtml);
                        if (string.IsNullOrWhiteSpace(headerText) && string.IsNullOrWhiteSpace(valueText))
                        {
                            continue;
                        }

                        var valueStyle = ParseStyle(valueCellHtml);
                        parsed.Add(new TitleLine(
                            HeaderText: headerText,
                            ValueText: valueText,
                            IsPair: true,
                            BreakBefore: breakBefore,
                            Style: valueStyle));
                        continue;
                    }

                    var text = cellMatches.Count > 0
                        ? string.Join("   ", cellMatches.Select(static m => CleanHtmlText(m.Groups[1].Value)))
                        : CleanHtmlText(rowHtml);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    var style = ParseStyle(rowHtml);
                    parsed.Add(new TitleLine(
                        HeaderText: string.Empty,
                        ValueText: text,
                        IsPair: false,
                        BreakBefore: breakBefore,
                        Style: style with { Bold = style.Bold || rowHtml.Contains("<b", StringComparison.OrdinalIgnoreCase) }));
                }

                if (parsed.Count > 0)
                {
                    return parsed;
                }
            }
        }

        return CleanHtmlText(title)
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(static line => new TitleLine(string.Empty, line, false, false, default))
            .ToList();
    }

    private static string CleanHtmlText(string text)
    {
        var normalized = text
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase);
        normalized = HtmlTagRegex.Replace(normalized, string.Empty);
        return WebUtility.HtmlDecode(normalized);
    }

    private static float ResolveFontSize(TitleLineStyle style, float defaultSize)
    {
        if (!style.FontSizePx.HasValue || style.FontSizePx.Value <= 0)
        {
            return defaultSize;
        }

        return Math.Max(8f, style.FontSizePx.Value);
    }

    private static SKTypeface ResolveTypeface(TitleLineStyle style)
    {
        var weight = style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant = style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        return SKTypeface.FromFamilyName(null, new SKFontStyle(weight, SKFontStyleWidth.Normal, slant));
    }

    private static RowMetrics BuildRowMetrics(IReadOnlyList<TitleLine> row, float defaultFontSize)
    {
        var fontSize = row.Count == 0
            ? defaultFontSize
            : row.Max(line => ResolveFontSize(line.Style, defaultFontSize));
        var lineHeight = fontSize * 1.25f;
        // BreakBefore means "start on next line", not "insert an empty extra line".
        var beforeGap = 0f;
        return new RowMetrics(fontSize, lineHeight, beforeGap);
    }

    private static List<List<TitleLine>> BuildVisualRows(IReadOnlyList<TitleLine> lines)
    {
        var rows = new List<List<TitleLine>>();
        var current = new List<TitleLine>();
        foreach (var line in lines)
        {
            if (line.BreakBefore && current.Count > 0)
            {
                rows.Add(current);
                current = new List<TitleLine>();
            }

            current.Add(line);
        }

        if (current.Count > 0)
        {
            rows.Add(current);
        }

        return rows;
    }

    private SegmentLayout BuildSegmentLayout(TitleLine line, float fontSize)
    {
        var headerStyle = new TitleLineStyle(null, null, Bold: true, Italic: false, FontSizePx: fontSize);
        var valueStyle = line.Style with { FontSizePx = fontSize };
        using var headerFont = new SKFont(ResolveTypeface(headerStyle), fontSize);
        using var valueFont = new SKFont(ResolveTypeface(valueStyle), fontSize);
        var headerText = line.HeaderText ?? string.Empty;
        var valueText = line.ValueText ?? string.Empty;
        if (line.IsPair)
        {
            var headerWidth = headerFont.MeasureText(headerText);
            var valueWidth = valueFont.MeasureText(valueText);
            var innerGap = 8f * PlotUiScale;
            return new SegmentLayout(
                HasHeader: true,
                HeaderText: headerText,
                ValueText: valueText,
                HeaderStyle: headerStyle,
                ValueStyle: valueStyle,
                ValueForegroundColor: valueStyle.ForegroundColor,
                ValueBackgroundColor: valueStyle.BackgroundColor,
                ValueOffsetX: headerWidth + innerGap,
                ValueWidth: valueWidth,
                Width: headerWidth + innerGap + valueWidth);
        }

        var plainWidth = valueFont.MeasureText(valueText);
        return new SegmentLayout(
            HasHeader: false,
            HeaderText: string.Empty,
            ValueText: valueText,
            HeaderStyle: headerStyle,
            ValueStyle: valueStyle,
            ValueForegroundColor: valueStyle.ForegroundColor,
            ValueBackgroundColor: valueStyle.BackgroundColor,
            ValueOffsetX: 0f,
            ValueWidth: plainWidth,
            Width: plainWidth);
    }

    private static TitleLineStyle ParseStyle(string html)
    {
        var match = HtmlStyleRegex.Match(html);
        if (!match.Success)
        {
            return default;
        }

        SKColor? fg = null;
        SKColor? bg = null;
        float? fontSize = null;
        var bold = false;
        var italic = false;
        var parts = match.Groups["style"].Value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var colon = part.IndexOf(':');
            if (colon <= 0 || colon >= part.Length - 1)
            {
                continue;
            }

            var key = part[..colon].Trim().ToLowerInvariant();
            var value = part[(colon + 1)..].Trim();
            switch (key)
            {
                case "color":
                    fg = TryParseCssColor(value);
                    break;
                case "background-color":
                    bg = TryParseCssColor(value);
                    break;
                case "font-style":
                    italic = value.Contains("italic", StringComparison.OrdinalIgnoreCase);
                    break;
                case "font-weight":
                    bold = value.Contains("bold", StringComparison.OrdinalIgnoreCase);
                    break;
                case "font-size":
                    var sizeMatch = HtmlFontSizeRegex.Match(value);
                    if (sizeMatch.Success &&
                        float.TryParse(sizeMatch.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size))
                    {
                        fontSize = size;
                    }
                    break;
            }
        }

        return new TitleLineStyle(fg, bg, bold, italic, fontSize);
    }

    private static SKColor? TryParseCssColor(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (SKColor.TryParse(value, out var skColor))
        {
            return skColor;
        }

        try
        {
            var parsed = System.Windows.Media.ColorConverter.ConvertFromString(value);
            if (parsed is System.Windows.Media.Color wpfColor)
            {
                return new SKColor(wpfColor.R, wpfColor.G, wpfColor.B, wpfColor.A);
            }
        }
        catch
        {
            // Ignore parse failures.
        }

        return null;
    }

    private readonly record struct TitleLine(
        string HeaderText,
        string ValueText,
        bool IsPair,
        bool BreakBefore,
        TitleLineStyle Style);

    private readonly record struct TitleLineStyle(
        SKColor? ForegroundColor,
        SKColor? BackgroundColor,
        bool Bold,
        bool Italic,
        float? FontSizePx);

    private readonly record struct RowMetrics(float FontSize, float LineHeight, float BeforeGap);

    private readonly record struct SegmentLayout(
        bool HasHeader,
        string HeaderText,
        string ValueText,
        TitleLineStyle HeaderStyle,
        TitleLineStyle ValueStyle,
        SKColor? ValueForegroundColor,
        SKColor? ValueBackgroundColor,
        float ValueOffsetX,
        float ValueWidth,
        float Width);

    protected virtual void DrawAxes(SKCanvas canvas, SKRect plotRect)
    {
        if (!ShowXAxis && !ShowYAxis)
        {
            return;
        }

        var zs = PlotUiScale;
        var axOff = ChartPlotLayout.AxisLabelInsetFromPlotLeft(zs);
        var axisMetrics = AxisFont.Metrics;
        var axisFontHeight = Math.Max(1f, -axisMetrics.Ascent + axisMetrics.Descent);
        var xTickTop = plotRect.Bottom + (axisFontHeight * 0.2f);
        var xTickBaseline = xTickTop - axisMetrics.Ascent;
        var xTickLength = Math.Max(3f * zs, axisFontHeight * 0.35f);
        var xAxisReserveY = ShowXAxis
            ? ChartPlotLayout.ComputeXAxisReserveY(zs, AxisFontSize, !string.IsNullOrWhiteSpace(AxisLabelX))
            : 0f;
        var yTickRight = plotRect.Left - Math.Max(axOff, axisFontHeight * 0.5f);
        var yTickLength = Math.Max(3f * zs, axisFontHeight * 0.35f);
        if (ShowXAxis)
        {
            canvas.DrawLine(plotRect.Left, plotRect.Bottom, plotRect.Right, plotRect.Bottom, AxisStrokePaint);
            var minGap = 0f;
            var maxXTickCount = Math.Clamp((int)(plotRect.Width / Math.Max(10f * zs, axisFontHeight * 0.9f)), 2, 120);
            var bestXTickRequest = 2;
            var xTicks = NiceAxisTickGrid.Generate(Viewport.XMin, Viewport.XMax, bestXTickRequest);
            for (var candidateTickCount = 3; candidateTickCount <= maxXTickCount; candidateTickCount++)
            {
                var candidateTicks = NiceAxisTickGrid.Generate(Viewport.XMin, Viewport.XMax, candidateTickCount, out var candStep);
                var lastRight = float.NegativeInfinity;
                var overlaps = false;
                foreach (var candidateTick in candidateTicks)
                {
                    if (!NonNegativeAxisTickPolicy.AllowsLabel(candidateTick))
                    {
                        continue;
                    }

                    var x = ToPixelX(candidateTick, plotRect);
                    var label = AxisTickLabelFormatter.FormatSnappedToGrid(candidateTick, candStep, AxisLabelPrecisionX, null, AxisLabelFormatX);
                    var width = AxisFont.MeasureText(label);
                    var left = x - (width * 0.5f);
                    left = Math.Clamp(left, plotRect.Left, plotRect.Right - width);
                    if (left < lastRight + minGap)
                    {
                        overlaps = true;
                        break;
                    }

                    lastRight = left + width;
                }

                if (overlaps)
                {
                    break;
                }

                xTicks = candidateTicks;
                bestXTickRequest = candidateTickCount;
            }

            xTicks = NiceAxisTickGrid.Generate(Viewport.XMin, Viewport.XMax, bestXTickRequest, out var xStep);
            var lastLabelRight = float.NegativeInfinity;
            foreach (var tick in xTicks)
            {
                if (!NonNegativeAxisTickPolicy.AllowsLabel(tick))
                {
                    continue;
                }

                var x = ToPixelX(tick, plotRect);
                canvas.DrawLine(x, plotRect.Bottom, x, plotRect.Bottom + xTickLength, AxisStrokePaint);
                var label = AxisTickLabelFormatter.FormatSnappedToGrid(tick, xStep, AxisLabelPrecisionX, null, AxisLabelFormatX);
                var width = AxisFont.MeasureText(label);
                var left = x - (width * 0.5f);
                left = Math.Clamp(left, plotRect.Left, plotRect.Right - width);
                var right = left + width;
                if (left < lastLabelRight + minGap)
                {
                    continue;
                }

                canvas.DrawText(label, left, xTickBaseline, SKTextAlign.Left, AxisFont, AxisLabelPaint);
                lastLabelRight = right;
            }
        }

        double yLabelScale = 1;
        var yPowExponent = 0;
        var useYPowerOfTen = false;

        if (ShowYAxis)
        {
            useYPowerOfTen = YAxisPowerOfTenFormatting.TryGetScale(Viewport.YMin, Viewport.YMax, out yLabelScale, out yPowExponent);
            canvas.DrawLine(plotRect.Left, plotRect.Top, plotRect.Left, plotRect.Bottom, AxisStrokePaint);
            var yMinGap = 3f * zs;
            var maxYTickCount = Math.Clamp((int)(plotRect.Height / Math.Max(10f * zs, axisFontHeight * 0.9f)), 2, 120);
            var bestYTickRequest = 2;
            var yTicks = NiceAxisTickGrid.Generate(Viewport.YMin, Viewport.YMax, bestYTickRequest);
            for (var candidateTickCount = 3; candidateTickCount <= maxYTickCount; candidateTickCount++)
            {
                var candidateTicks = NiceAxisTickGrid.Generate(Viewport.YMin, Viewport.YMax, candidateTickCount, out _);
                // Ticks run low→high data Y = bottom→top of plot = decreasing screen Y; compare each label's
                // bottom to the previous label's top (not top vs previous bottom — that skips every label after the first).
                var lastTopProbe = float.PositiveInfinity;
                var overlaps = false;
                foreach (var candidateTick in candidateTicks)
                {
                    if (!NonNegativeAxisTickPolicy.AllowsLabel(candidateTick))
                    {
                        continue;
                    }

                    var py = ToPixelY(candidateTick, plotRect);
                    var baselineProbe = py - (axisMetrics.Ascent + axisMetrics.Descent) * 0.5f;
                    var topProbe = baselineProbe + axisMetrics.Ascent;
                    var bottomProbe = baselineProbe + axisMetrics.Descent;
                    if (bottomProbe >= lastTopProbe - yMinGap)
                    {
                        overlaps = true;
                        break;
                    }

                    lastTopProbe = topProbe;
                }

                if (overlaps)
                {
                    break;
                }

                yTicks = candidateTicks;
                bestYTickRequest = candidateTickCount;
            }

            yTicks = NiceAxisTickGrid.Generate(Viewport.YMin, Viewport.YMax, bestYTickRequest, out var yStep);
            SKRect yExponentBadgePadded = default;
            var hasYExponentBadge = false;
            if (useYPowerOfTen)
            {
                var bb = YAxisPowerOfTenFormatting.GetExponentBadgeBounds(plotRect, zs, AxisFont, yPowExponent);
                var pad = 2f * zs;
                yExponentBadgePadded = SKRect.Create(
                    bb.Left - pad,
                    bb.Top - pad,
                    bb.Width + 2f * pad,
                    bb.Height + 2f * pad);
                hasYExponentBadge = true;
            }

            var lastLabelTop = float.PositiveInfinity;
            foreach (var tick in yTicks)
            {
                if (!NonNegativeAxisTickPolicy.AllowsLabel(tick))
                {
                    continue;
                }

                var y = ToPixelY(tick, plotRect);
                canvas.DrawLine(plotRect.Left - yTickLength, y, plotRect.Left, y, AxisStrokePaint);
                var baseline = y - (axisMetrics.Ascent + axisMetrics.Descent) * 0.5f;
                var top = baseline + axisMetrics.Ascent;
                var bottom = baseline + axisMetrics.Descent;
                if (bottom >= lastLabelTop - yMinGap)
                {
                    continue;
                }

                var label = useYPowerOfTen
                    ? AxisTickLabelFormatter.FormatSnappedToGrid(
                        tick / yLabelScale,
                        yStep / yLabelScale,
                        5,
                        null,
                        AxisLabelFormatY)
                    : AxisTickLabelFormatter.FormatSnappedToGrid(tick, yStep, AxisLabelPrecisionY, null, AxisLabelFormatY);
                if (hasYExponentBadge)
                {
                    var lw = AxisFont.MeasureText(label);
                    var lblTop = baseline + axisMetrics.Ascent;
                    var lblH = Math.Max(1e-3f, axisMetrics.Descent - axisMetrics.Ascent);
                    var lblRect = SKRect.Create(yTickRight - lw, lblTop, lw, lblH);
                    if (lblRect.IntersectsWith(yExponentBadgePadded))
                    {
                        continue;
                    }
                }

                canvas.DrawText(label, yTickRight, baseline, SKTextAlign.Right, AxisFont, AxisLabelPaint);
                lastLabelTop = top;
            }

            if (useYPowerOfTen)
            {
                YAxisPowerOfTenFormatting.DrawYAxisExponentBadge(canvas, plotRect, zs, AxisFont, AxisLabelPaint, yPowExponent);
            }
        }

        if (ShowXAxis && !string.IsNullOrWhiteSpace(AxisLabelX))
        {
            var cellBottom = plotRect.Bottom + xAxisReserveY;
            var titleBottom = cellBottom - (2f * zs) + (axisFontHeight * 0.3f);
            var xTitleBaseline = titleBottom - axisMetrics.Descent;
            canvas.DrawText(AxisLabelX, (plotRect.Left + plotRect.Right) / 2f, xTitleBaseline, SKTextAlign.Center, AxisFont, AxisLabelPaint);
        }

        if (ShowYAxis && !string.IsNullOrWhiteSpace(AxisLabelY))
        {
            var yCaption = AxisLabelY;
            var yTitleLeftInset = (ChartPlotLayout.CellPadding * zs) + (2f * zs);
            var yTitleCenterX = yTitleLeftInset + Math.Max(0f, -axisMetrics.Ascent) - (axisFontHeight * 0.5f);
            var titleCenterY = (plotRect.Top + plotRect.Bottom) * 0.5f;
            var drawYAxisCaption = true;
            if (useYPowerOfTen)
            {
                var bb = YAxisPowerOfTenFormatting.GetExponentBadgeBounds(plotRect, zs, AxisFont, yPowExponent);
                var pad = 2f * zs;
                var badgePadded = SKRect.Create(
                    bb.Left - pad,
                    bb.Top - pad,
                    bb.Width + 2f * pad,
                    bb.Height + 2f * pad);
                var textW = AxisFont.MeasureText(yCaption);
                var halfW = textW * 0.5f;
                var halfH = axisFontHeight * 0.5f;

                bool RotatedCaptionOverlapsBadge(float cy)
                {
                    var ax0 = yTitleCenterX - halfH;
                    var ay0 = cy - halfW;
                    var titleBounds = SKRect.Create(ax0, ay0, axisFontHeight, textW);
                    return titleBounds.IntersectsWith(badgePadded);
                }

                if (RotatedCaptionOverlapsBadge(titleCenterY))
                {
                    var step = Math.Max(4f * zs, axisFontHeight * 0.35f);
                    var limit = plotRect.Bottom + halfW + 6f * zs;
                    while (RotatedCaptionOverlapsBadge(titleCenterY) && titleCenterY < limit)
                    {
                        titleCenterY += step;
                    }

                    if (RotatedCaptionOverlapsBadge(titleCenterY))
                    {
                        drawYAxisCaption = false;
                    }
                }
            }

            if (drawYAxisCaption)
            {
                canvas.Save();
                canvas.Translate(yTitleCenterX, titleCenterY);
                canvas.RotateDegrees(-90f);
                canvas.DrawText(yCaption, 0f, 0f, SKTextAlign.Center, AxisFont, AxisLabelPaint);
                canvas.Restore();
            }
        }
    }

    protected void DrawFilledXRange(SKCanvas canvas, SKRect plotRect, double fromX, double toX) =>
        ChartSkiaXRangeHighlight.DrawFilledBand(canvas, plotRect, fromX, toX, ToPixelX, _overlayFill, _overlayStroke);
}
