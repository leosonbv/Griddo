using Plotto.Charting.Core;
using SkiaSharp;

namespace Plotto.Charting.Rendering;

/// <summary>Pixel Y range to omit when drawing a vertical marker through TIC overlay labels.</summary>
public readonly record struct VerticalMarkerLabelGap(float GapTopY, float GapBottomY)
{
    public static VerticalMarkerLabelGap None => new(float.PositiveInfinity, float.NegativeInfinity);

    public bool HasGap => GapTopY < GapBottomY;
}

/// <summary>Draws plain-text peak labels near integration anchors with simple collision avoidance.</summary>
internal static class ChartSkiaPeakLabels
{
    private const int MaxTicLabelLayoutCacheEntries = 512;

    private static readonly Dictionary<TicLabelLayoutCacheKey, TicLabelLayout> TicLabelLayoutCache = new();
    private static readonly Dictionary<int, FontLineMetrics> FontLineMetricsCache = new();
    private static readonly Dictionary<int, SKFont> TicOverlayFontCache = new();

    /// <summary>Per-chart cache for TIC overlay placement; avoids thrashing a global cache across many grid rows.</summary>
    internal sealed class TicOverlayPlacementCache
    {
        private TicOverlayPlacementCacheKey _drawKey;
        private List<TicOverlayDrawItem>? _drawItems;
        private List<TicLabelPlacement>? _placements;
        private TicOverlayPlacementCacheKey _extentsKey;
        private float _extentsMinTop;
        private float _extentsMaxBottom;
        private bool _hasExtents;

        internal IReadOnlyList<TicOverlayDrawItem> GetOrBuildDrawItems(
            SKRect plotRect,
            IReadOnlyList<ChromatogramPeakLabel> labels,
            double uiScale,
            double fontSizeDip,
            Func<double, SKRect, float> toPixelX,
            Func<double, SKRect, float> toPixelY,
            int peakLabelRotateDegrees)
        {
            var key = CreateTicOverlayPlacementCacheKey(
                plotRect,
                labels,
                uiScale,
                fontSizeDip,
                peakLabelRotateDegrees);
            if (_drawItems is not null && key.Equals(_drawKey))
            {
                return _drawItems;
            }

            _drawItems = BuildTicOverlayDrawItems(
                plotRect,
                labels,
                uiScale,
                fontSizeDip,
                toPixelX,
                toPixelY,
                peakLabelRotateDegrees);
            _drawKey = key;
            if (_drawItems.Count == 0)
            {
                _placements = [];
            }
            else
            {
                _placements = new List<TicLabelPlacement>(_drawItems.Count);
                foreach (var item in _drawItems)
                {
                    _placements.Add(item.Placement);
                }
            }

            return _drawItems;
        }

        internal IReadOnlyList<TicLabelPlacement> GetPlacements(
            SKRect plotRect,
            IReadOnlyList<ChromatogramPeakLabel> labels,
            double uiScale,
            double fontSizeDip,
            Func<double, SKRect, float> toPixelX,
            Func<double, SKRect, float> toPixelY,
            int peakLabelRotateDegrees)
        {
            _ = GetOrBuildDrawItems(
                plotRect,
                labels,
                uiScale,
                fontSizeDip,
                toPixelX,
                toPixelY,
                peakLabelRotateDegrees);
            return _placements ?? (IReadOnlyList<TicLabelPlacement>)Array.Empty<TicLabelPlacement>();
        }

        internal bool TryGetUncollidedExtents(
            SKRect plotRect,
            IReadOnlyList<ChromatogramPeakLabel> labels,
            double uiScale,
            double fontSizeDip,
            Func<double, SKRect, float> toPixelX,
            Func<double, SKRect, float> toPixelY,
            int peakLabelRotateDegrees,
            out float minTop,
            out float maxBottom)
        {
            var key = CreateTicOverlayPlacementCacheKey(
                plotRect,
                labels,
                uiScale,
                fontSizeDip,
                peakLabelRotateDegrees);
            if (_hasExtents && key.Equals(_extentsKey))
            {
                minTop = _extentsMinTop;
                maxBottom = _extentsMaxBottom;
                return true;
            }

            if (!TryGetTicOverlayLabelPixelExtentsUncollided(
                    plotRect,
                    labels,
                    uiScale,
                    fontSizeDip,
                    toPixelX,
                    toPixelY,
                    peakLabelRotateDegrees,
                    out minTop,
                    out maxBottom))
            {
                _hasExtents = false;
                return false;
            }

            _extentsKey = key;
            _extentsMinTop = minTop;
            _extentsMaxBottom = maxBottom;
            _hasExtents = true;
            return true;
        }
    }

    public static void Draw(
        SKCanvas canvas,
        SKRect plotRect,
        IReadOnlyList<ChromatogramPeakLabel> labels,
        double uiScale,
        double fontSizeDip,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        SKPaint textPaint,
        SKPaint? connectorPaint = null,
        int peakLabelRotateDegrees = 0,
        bool ticOverlayMode = false,
        bool showDebugRect = false,
        TicOverlayPlacementCache? ticOverlayCache = null)
    {
        if (plotRect.Width <= 1f || plotRect.Height <= 1f || labels.Count == 0)
        {
            return;
        }

        if (ticOverlayMode)
        {
            DrawTicOverlayLabels(
                canvas,
                plotRect,
                labels,
                uiScale,
                fontSizeDip,
                toPixelX,
                toPixelY,
                textPaint,
                peakLabelRotateDegrees,
                showDebugRect,
                ticOverlayCache);
            return;
        }

        DrawConnectorLabels(
            canvas,
            plotRect,
            labels,
            uiScale,
            fontSizeDip,
            toPixelX,
            toPixelY,
            textPaint,
            connectorPaint);
    }

    private static void DrawTicOverlayLabels(
        SKCanvas canvas,
        SKRect plotRect,
        IReadOnlyList<ChromatogramPeakLabel> labels,
        double uiScale,
        double fontSizeDip,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        SKPaint textPaint,
        int peakLabelRotateDegrees,
        bool showDebugRect,
        TicOverlayPlacementCache? ticOverlayCache)
    {
        if (plotRect.Width <= 1f || plotRect.Height <= 1f || labels.Count == 0)
        {
            return;
        }

        var fontPx = (float)Math.Clamp(fontSizeDip, 6d, 24d) * (float)uiScale;
        var font = GetTicOverlayFont(fontPx);

        using var missingPeakPaint = new SKPaint
        {
            IsAntialias = textPaint.IsAntialias,
            Color = new SKColor(80, 140, 255),
            Style = textPaint.Style
        };

        var drawItems = ticOverlayCache?.GetOrBuildDrawItems(
            plotRect,
            labels,
            uiScale,
            fontSizeDip,
            toPixelX,
            toPixelY,
            peakLabelRotateDegrees)
            ?? BuildTicOverlayDrawItems(
                plotRect,
                labels,
                uiScale,
                fontSizeDip,
                toPixelX,
                toPixelY,
                peakLabelRotateDegrees);
        foreach (var item in drawItems)
        {
            var placement = item.Placement;
            canvas.Save();
            canvas.Translate(placement.AnchorX, placement.AnchorY);
            canvas.RotateDegrees(peakLabelRotateDegrees);
            canvas.Translate(-placement.AnchorLocal.X, -placement.AnchorLocal.Y);

            if (showDebugRect)
            {
                DrawTicOverlayDebugRect(canvas, placement, (float)uiScale);
            }

            var paint = item.Label.PeakFound ? textPaint : missingPeakPaint;
            var baseline = item.Layout.FirstBaseline;
            var centerX = item.Layout.Width * 0.5f;
            foreach (var line in item.Lines)
            {
                canvas.DrawText(line, centerX, baseline, SKTextAlign.Center, font, paint);
                baseline += item.Layout.LineStep;
            }

            canvas.Restore();
        }
    }

    internal readonly record struct TicOverlayDrawItem(
        ChromatogramPeakLabel Label,
        TicLabelLayout Layout,
        TicLabelPlacement Placement,
        string[] Lines);

    private readonly record struct TicOverlayPlacementCacheKey(
        int PlotLeftBits,
        int PlotTopBits,
        int PlotWidthBits,
        int PlotHeightBits,
        int UiScaleBits,
        int FontSizeBits,
        int RotateDegrees,
        int LabelCount,
        long LabelsHash);

    internal readonly record struct TicLabelPlacement(
        float AnchorX,
        float AnchorY,
        float Width,
        float TopLocalY,
        float BottomLocalY,
        float Degrees,
        SKPoint AnchorLocal,
        float ScreenLeft,
        float ScreenTop,
        float ScreenRight,
        float ScreenBottom,
        float ScreenCenterX,
        float ScreenCenterY,
        float HalfWidth,
        float HalfHeight,
        SKPoint ScreenCorner0,
        SKPoint ScreenCorner1,
        SKPoint ScreenCorner2,
        SKPoint ScreenCorner3);

    internal readonly record struct TicLabelLayout(
        float Width,
        float TopLocalY,
        float BottomLocalY,
        float FirstBaseline,
        float LineStep,
        float LastLineBottom = 0f,
        float TextAscent = 0f,
        float TextDescent = 0f);

    private readonly record struct FontLineMetrics(float Ascent, float Descent, float LineStep);

    private readonly record struct TicLabelLayoutCacheKey(int FontSizeBits, int PadBits, string Text);

    private static string NormalizeTicLabelText(string text) =>
        text.Trim().Replace("\r\n", "\n", StringComparison.Ordinal);

    private static SKFont GetTicOverlayFont(float fontPx)
    {
        var key = BitConverter.SingleToInt32Bits(fontPx);
        if (!TicOverlayFontCache.TryGetValue(key, out var font))
        {
            var typeface = SKTypeface.FromFamilyName(null);
            font = new SKFont(typeface, fontPx);
            TicOverlayFontCache[key] = font;
        }

        return font;
    }

    private static FontLineMetrics GetFontLineMetrics(SKFont font)
    {
        var sizeBits = BitConverter.SingleToInt32Bits(font.Size);
        if (FontLineMetricsCache.TryGetValue(sizeBits, out var cached))
        {
            return cached;
        }

        font.GetFontMetrics(out var metrics);
        var ascent = -metrics.Ascent;
        var descent = metrics.Descent;
        var lineStep = ascent + descent;
        if (lineStep <= 0f)
        {
            lineStep = font.Size;
        }

        cached = new FontLineMetrics(ascent, descent, lineStep);
        FontLineMetricsCache[sizeBits] = cached;
        return cached;
    }

    private static TicLabelLayout MeasureTicLabelLayout(SKFont font, string normalizedText, float pad)
    {
        var cacheKey = new TicLabelLayoutCacheKey(
            BitConverter.SingleToInt32Bits(font.Size),
            BitConverter.SingleToInt32Bits(pad),
            normalizedText);
        if (TicLabelLayoutCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var layout = MeasureTicLabelLayoutUncached(font, normalizedText, pad);
        if (TicLabelLayoutCache.Count >= MaxTicLabelLayoutCacheEntries)
        {
            TicLabelLayoutCache.Clear();
        }

        TicLabelLayoutCache[cacheKey] = layout;
        return layout;
    }

    private static TicLabelLayout MeasureTicLabelLayoutUncached(SKFont font, string normalizedText, float pad)
    {
        var lines = normalizedText.Split('\n');
        var lineCount = lines.Length;
        if (lineCount == 0)
        {
            return default;
        }

        var metrics = GetFontLineMetrics(font);
        var ascent = metrics.Ascent;
        var descent = metrics.Descent;
        var lineStep = metrics.LineStep;

        var padX = pad;
        var padY = pad * 0.85f;

        float blockW = 0f;
        foreach (var line in lines)
        {
            blockW = Math.Max(blockW, font.MeasureText(line));
        }

        var lastBaseline = -padY - descent;
        var firstBaseline = lastBaseline - (lineCount - 1) * lineStep;
        var blockTop = firstBaseline - ascent;
        var blockBottom = lastBaseline + descent;
        var topLocalY = blockTop - padY;
        var bottomLocalY = blockBottom + padY;
        var wBox = blockW + 2f * padX;
        var lastLineBottom = lastBaseline + descent;
        return new TicLabelLayout(
            wBox,
            topLocalY,
            bottomLocalY,
            firstBaseline,
            lineStep,
            lastLineBottom,
            ascent,
            descent);
    }

    private static SKPoint GetAnchorLocal(float width, float topLocalY, float bottomLocalY, float degrees)
    {
        ReadOnlySpan<SKPoint> corners =
        [
            new(0f, bottomLocalY),
            new(width, bottomLocalY),
            new(width, topLocalY),
            new(0f, topLocalY)
        ];

        var rad = degrees * MathF.PI / 180f;
        var cos = MathF.Cos(rad);
        var sin = MathF.Sin(rad);
        var maxY = float.MinValue;
        foreach (var corner in corners)
        {
            maxY = MathF.Max(maxY, RotateY(corner, cos, sin));
        }

        const float tolerance = 0.05f;
        var sumX = 0f;
        var sumY = 0f;
        var count = 0;
        foreach (var corner in corners)
        {
            if (RotateY(corner, cos, sin) < maxY - tolerance)
            {
                continue;
            }

            sumX += corner.X;
            sumY += corner.Y;
            count++;
        }

        return count > 0
            ? new SKPoint(sumX / count, sumY / count)
            : corners[0];
    }

    private static bool IsNearHorizontalRotation(int peakLabelRotateDegrees)
    {
        var normalized = ((peakLabelRotateDegrees % 360) + 360) % 360;
        return normalized <= 15 || normalized >= 345;
    }

    private static bool TryCreateTicOverlayPlacement(
        ChromatogramPeakLabel label,
        TicLabelLayout layout,
        int lineCount,
        SKRect plotRect,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        int peakLabelRotateDegrees,
        float uiScale,
        out TicLabelPlacement placement)
    {
        placement = default;
        if (!double.IsFinite(label.X) || !double.IsFinite(label.Y))
        {
            return false;
        }

        var gap = 4f * uiScale;
        var apexPixelY = toPixelY(label.Y, plotRect);
        var apexPixelX = toPixelX(label.X, plotRect);

        var w = layout.Width;
        var bot = layout.BottomLocalY;
        var top = layout.TopLocalY;

        // anchorX and anchorY are computed below after determining the 2 lowest rotated corners.

        // For vertical alignment: the 2 lowest corners (after rotation) should be at apexPixelY - gap.
        // Always use center-bottom as AnchorLocal.
        var anchorLocalX = w * 0.5f;
        var anchorLocalY = bot;

        // Compute rotation matrix.
        var rad = peakLabelRotateDegrees * MathF.PI / 180f;
        var cosA = MathF.Cos(rad);
        var sinA = MathF.Sin(rad);

        // The 4 local corners, relative to AnchorLocal.
        var bl_relX = 0f - anchorLocalX;
        var bl_relY = bot - anchorLocalY;
        var br_relX = w - anchorLocalX;
        var br_relY = bot - anchorLocalY;
        var tr_relX = w - anchorLocalX;
        var tr_relY = top - anchorLocalY;
        var tl_relX = 0f - anchorLocalX;
        var tl_relY = top - anchorLocalY;

        // Screen offsets (relative to anchor) after rotation.
        var bl_screenX = bl_relX * cosA - bl_relY * sinA;
        var bl_screenY = bl_relX * sinA + bl_relY * cosA;
        var br_screenX = br_relX * cosA - br_relY * sinA;
        var br_screenY = br_relX * sinA + br_relY * cosA;
        var tr_screenX = tr_relX * cosA - tr_relY * sinA;
        var tr_screenY = tr_relX * sinA + tr_relY * cosA;
        var tl_screenX = tl_relX * cosA - tl_relY * sinA;
        var tl_screenY = tl_relX * sinA + tl_relY * cosA;

        ReadOnlySpan<(float Item1, float Item2)> corners =
        [
            (bl_screenX, bl_screenY),
            (br_screenX, br_screenY),
            (tr_screenX, tr_screenY),
            (tl_screenX, tl_screenY),
        ];

        // Find the 2 corners with highest screen Y (lowest on screen).
        var idx0 = 0;
        var idx1 = 1;
        var bestY0 = corners[0].Item2;
        var bestY1 = corners[1].Item2;
        if (bestY1 > bestY0)
        {
            (idx0, idx1) = (idx1, idx0);
            (bestY0, bestY1) = (bestY1, bestY0);
        }

        for (var i = 2; i < corners.Length; i++)
        {
            var y = corners[i].Item2;
            if (y > bestY0)
            {
                bestY1 = bestY0;
                idx1 = idx0;
                bestY0 = y;
                idx0 = i;
            }
            else if (y > bestY1)
            {
                bestY1 = y;
                idx1 = i;
            }
        }

        var avgScreenX = (corners[idx0].Item1 + corners[idx1].Item1) * 0.5f;
        var avgScreenY = (corners[idx0].Item2 + corners[idx1].Item2) * 0.5f;

        // Position the anchor so that:
        // - The average X of the 2 lowest rotated corners is aligned with the apex X.
        // - The average Y of the 2 lowest rotated corners is at apexPixelY - gap.
        var anchorX = apexPixelX - avgScreenX;
        var anchorY = apexPixelY - gap - avgScreenY;

        var screenCorner0 = new SKPoint(anchorX + bl_screenX, anchorY + bl_screenY);
        var screenCorner1 = new SKPoint(anchorX + br_screenX, anchorY + br_screenY);
        var screenCorner2 = new SKPoint(anchorX + tr_screenX, anchorY + tr_screenY);
        var screenCorner3 = new SKPoint(anchorX + tl_screenX, anchorY + tl_screenY);
        var screenLeft = MathF.Min(MathF.Min(screenCorner0.X, screenCorner1.X), MathF.Min(screenCorner2.X, screenCorner3.X));
        var screenRight = MathF.Max(MathF.Max(screenCorner0.X, screenCorner1.X), MathF.Max(screenCorner2.X, screenCorner3.X));
        var screenTop = MathF.Min(MathF.Min(screenCorner0.Y, screenCorner1.Y), MathF.Min(screenCorner2.Y, screenCorner3.Y));
        var screenBottom = MathF.Max(MathF.Max(screenCorner0.Y, screenCorner1.Y), MathF.Max(screenCorner2.Y, screenCorner3.Y));

        placement = new TicLabelPlacement(
            anchorX,
            anchorY,
            w,
            top,
            bot,
            peakLabelRotateDegrees,
            new SKPoint(anchorLocalX, anchorLocalY),
            screenLeft,
            screenTop,
            screenRight,
            screenBottom,
            (screenCorner0.X + screenCorner1.X + screenCorner2.X + screenCorner3.X) * 0.25f,
            (screenCorner0.Y + screenCorner1.Y + screenCorner2.Y + screenCorner3.Y) * 0.25f,
            w * 0.5f,
            (bot - top) * 0.5f,
            screenCorner0,
            screenCorner1,
            screenCorner2,
            screenCorner3);
        return true;
    }

    private static void DrawTicOverlayDebugRect(SKCanvas canvas, TicLabelPlacement placement, float uiScale)
    {
        using var debugPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(255, 90, 90, 220),
            StrokeWidth = Math.Max(1f, 1f * uiScale)
        };
        var top = placement.TopLocalY;
        var bottom = placement.BottomLocalY;
        var width = placement.Width;
        canvas.DrawLine(0f, bottom, width, bottom, debugPaint);
        canvas.DrawLine(width, bottom, width, top, debugPaint);
        canvas.DrawLine(width, top, 0f, top, debugPaint);
        canvas.DrawLine(0f, top, 0f, bottom, debugPaint);
    }

    private static bool TryGetMaxScreenYOnVerticalSlice(
        TicLabelPlacement placement,
        float markerPixelX,
        float pad,
        out float maxScreenY)
    {
        maxScreenY = float.MinValue;
        Span<SKPoint> corners = stackalloc SKPoint[4];
        GetScreenCorners(placement, corners);

        var found = false;
        for (var i = 0; i < corners.Length; i++)
        {
            var p1 = corners[i];
            var p2 = corners[(i + 1) % corners.Length];
            if (Math.Abs(p1.X - markerPixelX) <= pad)
            {
                maxScreenY = MathF.Max(maxScreenY, p1.Y);
                found = true;
            }

            if (Math.Abs(p2.X - markerPixelX) <= pad)
            {
                maxScreenY = MathF.Max(maxScreenY, p2.Y);
                found = true;
            }

            var minX = Math.Min(p1.X, p2.X) - pad;
            var maxX = Math.Max(p1.X, p2.X) + pad;
            if (markerPixelX < minX || markerPixelX > maxX)
            {
                continue;
            }

            if (MathF.Abs(p2.X - p1.X) < 1e-5f)
            {
                maxScreenY = MathF.Max(maxScreenY, MathF.Max(p1.Y, p2.Y));
                found = true;
                continue;
            }

            var t = (markerPixelX - p1.X) / (p2.X - p1.X);
            if (t < -0.001f || t > 1.001f)
            {
                continue;
            }

            maxScreenY = MathF.Max(maxScreenY, p1.Y + t * (p2.Y - p1.Y));
            found = true;
        }

        return found;
    }

    private static bool TryGetMinScreenYOnVerticalSlice(
        TicLabelPlacement placement,
        float markerPixelX,
        float pad,
        out float minScreenY)
    {
        minScreenY = float.MaxValue;
        Span<SKPoint> corners = stackalloc SKPoint[4];
        GetScreenCorners(placement, corners);

        var found = false;
        for (var i = 0; i < corners.Length; i++)
        {
            var p1 = corners[i];
            var p2 = corners[(i + 1) % corners.Length];
            if (Math.Abs(p1.X - markerPixelX) <= pad)
            {
                minScreenY = MathF.Min(minScreenY, p1.Y);
                found = true;
            }

            if (Math.Abs(p2.X - markerPixelX) <= pad)
            {
                minScreenY = MathF.Min(minScreenY, p2.Y);
                found = true;
            }

            var minX = Math.Min(p1.X, p2.X) - pad;
            var maxX = Math.Max(p1.X, p2.X) + pad;
            if (markerPixelX < minX || markerPixelX > maxX)
            {
                continue;
            }

            if (MathF.Abs(p2.X - p1.X) < 1e-5f)
            {
                minScreenY = MathF.Min(minScreenY, MathF.Min(p1.Y, p2.Y));
                found = true;
                continue;
            }

            var t = (markerPixelX - p1.X) / (p2.X - p1.X);
            if (t < -0.001f || t > 1.001f)
            {
                continue;
            }

            minScreenY = MathF.Min(minScreenY, p1.Y + t * (p2.Y - p1.Y));
            found = true;
        }

        return found;
    }

    private static bool IntersectsAnyRotated(
        TicLabelPlacement candidate,
        IReadOnlyList<TicLabelPlacement> occupied,
        float pad,
        float cos,
        float sin)
    {
        foreach (var other in occupied)
        {
            if (SameOrientationRectsOverlap(candidate, other, pad, cos, sin))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SameOrientationRectsOverlap(
        in TicLabelPlacement left,
        in TicLabelPlacement right,
        float pad,
        float cos,
        float sin)
    {
        if (left.ScreenRight + pad < right.ScreenLeft - pad
            || right.ScreenRight + pad < left.ScreenLeft - pad
            || left.ScreenBottom + pad < right.ScreenTop - pad
            || right.ScreenBottom + pad < left.ScreenTop - pad)
        {
            return false;
        }

        var dx = right.ScreenCenterX - left.ScreenCenterX;
        var dy = right.ScreenCenterY - left.ScreenCenterY;
        var localDx = MathF.Abs(dx * cos + dy * sin);
        var localDy = MathF.Abs(-dx * sin + dy * cos);
        return localDx <= left.HalfWidth + right.HalfWidth + pad
               && localDy <= left.HalfHeight + right.HalfHeight + pad;
    }

    private static void GetScreenCorners(TicLabelPlacement placement, Span<SKPoint> corners)
    {
        corners[0] = placement.ScreenCorner0;
        corners[1] = placement.ScreenCorner1;
        corners[2] = placement.ScreenCorner2;
        corners[3] = placement.ScreenCorner3;
    }

    private static float RotateX(float x, float y, float cos, float sin) => x * cos - y * sin;

    private static float RotateY(SKPoint point, float cos, float sin) => RotateY(point.X, point.Y, cos, sin);

    private static float RotateY(float x, float y, float cos, float sin) => x * sin + y * cos;

    private static void DrawConnectorLabels(
        SKCanvas canvas,
        SKRect plotRect,
        IReadOnlyList<ChromatogramPeakLabel> labels,
        double uiScale,
        double fontSizeDip,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        SKPaint textPaint,
        SKPaint? connectorPaint)
    {
        var fontPx = (float)Math.Clamp(fontSizeDip, 6d, 24d) * (float)uiScale;
        using var typeface = SKTypeface.FromFamilyName(null);
        using var font = new SKFont(typeface, fontPx);
        var lineHeight = fontPx * 1.25f;
        var pad = 4f * (float)uiScale;
        var margin = 6f * (float)uiScale;
        var bounds = SKRect.Create(
            plotRect.Left + margin,
            plotRect.Top + margin,
            Math.Max(0, plotRect.Width - 2f * margin),
            Math.Max(0, plotRect.Height - 2f * margin));
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var occupied = new List<SKRect>();
        var markerRadius = 6f * (float)uiScale;
        var standoff = markerRadius + 12f * (float)uiScale;

        foreach (var label in labels)
        {
            var text = label.LabelPlainText?.Trim() ?? string.Empty;
            if (text.Length == 0 || !double.IsFinite(label.X) || !double.IsFinite(label.Y))
            {
                continue;
            }

            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            float blockW = 0f;
            foreach (var line in lines)
            {
                blockW = Math.Max(blockW, font.MeasureText(line));
            }

            var blockH = lines.Length * lineHeight;
            var wBox = blockW + 2f * pad;
            var hBox = blockH + 2f * pad;
            var ax = toPixelX(label.X, plotRect);
            var ay = toPixelY(label.Y, plotRect);

            if (!TryPlaceLabelRect(ax, ay, wBox, hBox, standoff, bounds, occupied, out var rect))
            {
                continue;
            }

            occupied.Add(rect);
            if (connectorPaint is not null)
            {
                DrawConnector(canvas, new SKPoint(ax, ay), markerRadius, rect, connectorPaint);
            }

            var textLeft = rect.Left + pad;
            var baseline = rect.Top + pad + font.Size * 0.75f;
            foreach (var line in lines)
            {
                canvas.DrawText(line, textLeft, baseline, SKTextAlign.Left, font, textPaint);
                baseline += lineHeight;
            }
        }
    }

    private static bool TryPlaceLabelRect(
        float ax,
        float ay,
        float width,
        float height,
        float standoff,
        SKRect bounds,
        IReadOnlyList<SKRect> occupied,
        out SKRect rect)
    {
        rect = default;
        if (bounds.Width < width || bounds.Height < height)
        {
            return false;
        }

        for (var attempt = 0; attempt < 48; attempt++)
        {
            var angle = attempt * (MathF.PI / 12f);
            var radius = standoff + attempt * (10f * (height / 24f + 0.5f));
            var ox = MathF.Cos(angle) * radius;
            var oy = MathF.Sin(angle) * radius;
            var candidate = SKRect.Create(ax + ox - width * 0.5f, ay + oy - height * 0.5f, width, height);
            var clamped = ClampRect(candidate, bounds);
            if (!RectsApproximatelyEqual(candidate, clamped))
            {
                continue;
            }

            if (IntersectsAny(clamped, occupied, 4f))
            {
                continue;
            }

            rect = clamped;
            return true;
        }

        return false;
    }

    private static void DrawConnector(SKCanvas canvas, SKPoint markerCenter, float markerRadius, SKRect labelRect, SKPaint paint)
    {
        var labelCenter = new SKPoint(labelRect.MidX, labelRect.MidY);
        var start = PointOnCircleToward(markerCenter, markerRadius, labelCenter);
        if (!TryRayRectBorderIntersection(markerCenter, labelCenter, labelRect, out var end))
        {
            return;
        }

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (dx * dx + dy * dy < 1f)
        {
            return;
        }

        canvas.DrawLine(start, end, paint);
    }

    private static SKPoint PointOnCircleToward(SKPoint center, float radius, SKPoint toward)
    {
        var dx = toward.X - center.X;
        var dy = toward.Y - center.Y;
        var len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-3f)
        {
            return new SKPoint(center.X, center.Y - radius);
        }

        return new SKPoint(center.X + dx / len * radius, center.Y + dy / len * radius);
    }

    private static bool TryRayRectBorderIntersection(SKPoint origin, SKPoint toward, SKRect rect, out SKPoint hit)
    {
        hit = default;
        var dx = toward.X - origin.X;
        var dy = toward.Y - origin.Y;
        if (MathF.Abs(dx) < 1e-6f && MathF.Abs(dy) < 1e-6f)
        {
            return false;
        }

        var bestT = float.MaxValue;
        if (MathF.Abs(dx) > 1e-6f)
        {
            ConsiderXEdge(rect.Left);
            ConsiderXEdge(rect.Right);
        }

        if (MathF.Abs(dy) > 1e-6f)
        {
            ConsiderYEdge(rect.Top);
            ConsiderYEdge(rect.Bottom);
        }

        if (bestT >= float.MaxValue * 0.5f)
        {
            return false;
        }

        hit = new SKPoint(origin.X + dx * bestT, origin.Y + dy * bestT);
        return true;

        void ConsiderXEdge(float xEdge)
        {
            var t = (xEdge - origin.X) / dx;
            if (t <= 0f || t >= bestT)
            {
                return;
            }

            var y = origin.Y + dy * t;
            if (y >= rect.Top - 0.5f && y <= rect.Bottom + 0.5f)
            {
                bestT = t;
            }
        }

        void ConsiderYEdge(float yEdge)
        {
            var t = (yEdge - origin.Y) / dy;
            if (t <= 0f || t >= bestT)
            {
                return;
            }

            var x = origin.X + dx * t;
            if (x >= rect.Left - 0.5f && x <= rect.Right + 0.5f)
            {
                bestT = t;
            }
        }
    }

    private static SKRect ClampRect(SKRect rect, SKRect bounds)
    {
        var width = Math.Min(rect.Width, bounds.Width);
        var height = Math.Min(rect.Height, bounds.Height);
        var maxLeft = bounds.Right - width;
        var maxTop = bounds.Bottom - height;
        var left = maxLeft < bounds.Left ? bounds.Left : Math.Clamp(rect.Left, bounds.Left, maxLeft);
        var top = maxTop < bounds.Top ? bounds.Top : Math.Clamp(rect.Top, bounds.Top, maxTop);
        return SKRect.Create(left, top, width, height);
    }

    private static bool RectsApproximatelyEqual(SKRect a, SKRect b, float tolerance = 0.75f) =>
        MathF.Abs(a.Left - b.Left) <= tolerance
        && MathF.Abs(a.Top - b.Top) <= tolerance
        && MathF.Abs(a.Width - b.Width) <= tolerance
        && MathF.Abs(a.Height - b.Height) <= tolerance;

    private static bool IntersectsAny(SKRect rect, IReadOnlyList<SKRect> occupied, float pad)
    {
        var inflated = SKRect.Inflate(rect, pad, pad);
        foreach (var other in occupied)
        {
            if (inflated.IntersectsWith(other))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Top edge (smallest Y) of visible peak labels in plot pixel space.</summary>
    public static bool TryGetMinVisibleLabelTopPixelY(
        SKRect plotRect,
        IReadOnlyList<ChromatogramPeakLabel> labels,
        double uiScale,
        double fontSizeDip,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        int peakLabelRotateDegrees,
        bool ticOverlayMode,
        out float minTop,
        TicOverlayPlacementCache? ticOverlayCache = null)
    {
        minTop = float.MaxValue;
        if (plotRect.Width <= 1f || plotRect.Height <= 1f || labels.Count == 0)
        {
            return false;
        }

        if (ticOverlayMode)
        {
            if (ticOverlayCache is not null)
            {
                return ticOverlayCache.TryGetUncollidedExtents(
                    plotRect,
                    labels,
                    uiScale,
                    fontSizeDip,
                    toPixelX,
                    toPixelY,
                    peakLabelRotateDegrees,
                    out minTop,
                    out _);
            }

            return TryGetTicOverlayLabelPixelExtents(
                plotRect,
                labels,
                uiScale,
                fontSizeDip,
                toPixelX,
                toPixelY,
                peakLabelRotateDegrees,
                out minTop,
                out _);
        }

        return TryGetConnectorLabelPixelExtents(
            plotRect,
            labels,
            uiScale,
            fontSizeDip,
            toPixelX,
            toPixelY,
            out minTop,
            out _);
    }

    /// <summary>
    /// Label pixel-Y range a vertical marker at <paramref name="markerPixelX"/> should omit.
    /// Draw above <see cref="VerticalMarkerLabelGap.GapTopY"/> and below <see cref="VerticalMarkerLabelGap.GapBottomY"/>.
    /// </summary>
    public static bool TryGetVerticalMarkerLabelGapYAt(
        float markerPixelX,
        SKRect plotRect,
        IReadOnlyList<ChromatogramPeakLabel> labels,
        double uiScale,
        double fontSizeDip,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        int peakLabelRotateDegrees,
        bool ticOverlayMode,
        out VerticalMarkerLabelGap gap)
    {
        gap = VerticalMarkerLabelGap.None;
        if (plotRect.Width <= 1f || plotRect.Height <= 1f || labels.Count == 0)
        {
            return false;
        }

        var pad = 4f * (float)uiScale;
        if (ticOverlayMode)
        {
            return TryGetTicOverlayMarkerLabelGapAt(
                markerPixelX,
                plotRect,
                labels,
                uiScale,
                fontSizeDip,
                toPixelX,
                toPixelY,
                peakLabelRotateDegrees,
                pad,
                out gap);
        }

        return TryGetConnectorMarkerLabelGapAt(
            markerPixelX,
            plotRect,
            labels,
            uiScale,
            fontSizeDip,
            toPixelX,
            toPixelY,
            pad,
            out gap);
    }

    /// <summary>Lowest Y (largest pixel value) a vertical marker at <paramref name="markerPixelX"/> may reach without crossing a visible label.</summary>
    public static bool TryGetVerticalMarkerTopPixelYAt(
        float markerPixelX,
        SKRect plotRect,
        IReadOnlyList<ChromatogramPeakLabel> labels,
        double uiScale,
        double fontSizeDip,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        int peakLabelRotateDegrees,
        bool ticOverlayMode,
        out float topPixelY)
    {
        topPixelY = plotRect.Top;
        if (plotRect.Width <= 1f || plotRect.Height <= 1f || labels.Count == 0)
        {
            return false;
        }

        var pad = 4f * (float)uiScale;
        if (ticOverlayMode)
        {
            return TryGetTicOverlayMarkerTopAt(
                markerPixelX,
                plotRect,
                labels,
                uiScale,
                fontSizeDip,
                toPixelX,
                toPixelY,
                peakLabelRotateDegrees,
                pad,
                out topPixelY);
        }

        return TryGetConnectorMarkerTopAt(
            markerPixelX,
            plotRect,
            labels,
            uiScale,
            fontSizeDip,
            toPixelX,
            toPixelY,
            pad,
            out topPixelY);
    }

    /// <summary>Lowest Y (largest pixel value) markers may reach without crossing visible labels.</summary>
    public static bool TryGetVerticalMarkerTopPixelY(
        SKRect plotRect,
        IReadOnlyList<ChromatogramPeakLabel> labels,
        double uiScale,
        double fontSizeDip,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        int peakLabelRotateDegrees,
        bool ticOverlayMode,
        out float topPixelY)
    {
        topPixelY = plotRect.Top;
        if (plotRect.Width <= 1f || plotRect.Height <= 1f || labels.Count == 0)
        {
            return false;
        }

        var pad = 4f * (float)uiScale;
        if (ticOverlayMode)
        {
            if (!TryGetTicOverlayLabelPixelExtents(
                    plotRect,
                    labels,
                    uiScale,
                    fontSizeDip,
                    toPixelX,
                    toPixelY,
                    peakLabelRotateDegrees,
                    out _,
                    out var maxBottom))
            {
                return false;
            }

            topPixelY = maxBottom + pad;
            return topPixelY < plotRect.Bottom;
        }

        if (!TryGetConnectorLabelPixelExtents(
                plotRect,
                labels,
                uiScale,
                fontSizeDip,
                toPixelX,
                toPixelY,
                out _,
                out var connectorMaxBottom))
        {
            return false;
        }

        topPixelY = connectorMaxBottom + pad;
        return topPixelY < plotRect.Bottom;
    }

    private static TicOverlayPlacementCacheKey CreateTicOverlayPlacementCacheKey(
        SKRect plotRect,
        IReadOnlyList<ChromatogramPeakLabel> labels,
        double uiScale,
        double fontSizeDip,
        int peakLabelRotateDegrees)
    {
        var hash = new HashCode();
        hash.Add(labels.Count);
        foreach (var label in labels)
        {
            hash.Add(label.X);
            hash.Add(label.Y);
            hash.Add(label.LabelPlainText);
            hash.Add(label.PeakFound);
        }

        return new TicOverlayPlacementCacheKey(
            BitConverter.SingleToInt32Bits(plotRect.Left),
            BitConverter.SingleToInt32Bits(plotRect.Top),
            BitConverter.SingleToInt32Bits(plotRect.Width),
            BitConverter.SingleToInt32Bits(plotRect.Height),
            BitConverter.SingleToInt32Bits((float)uiScale),
            BitConverter.SingleToInt32Bits((float)fontSizeDip),
            peakLabelRotateDegrees,
            labels.Count,
            (long)hash.ToHashCode());
    }

    private static List<TicOverlayDrawItem> BuildTicOverlayDrawItems(
        SKRect plotRect,
        IReadOnlyList<ChromatogramPeakLabel> labels,
        double uiScale,
        double fontSizeDip,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        int peakLabelRotateDegrees)
    {
        var result = new List<TicOverlayDrawItem>();
        var fontPx = (float)Math.Clamp(fontSizeDip, 6d, 24d) * (float)uiScale;
        var font = GetTicOverlayFont(fontPx);
        var pad = 2f * (float)uiScale;
        var margin = 6f * (float)uiScale;
        var bounds = SKRect.Create(
            plotRect.Left + margin,
            plotRect.Top + margin,
            Math.Max(0, plotRect.Width - 2f * margin),
            Math.Max(0, plotRect.Height - 2f * margin));
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return result;
        }

        var rad = peakLabelRotateDegrees * MathF.PI / 180f;
        var cos = MathF.Cos(rad);
        var sin = MathF.Sin(rad);
        var collisionPad = 2f * (float)uiScale;
        var occupied = new List<TicLabelPlacement>();
        var ordered = CollectValidTicOverlayLabels(labels);

        foreach (var label in ordered)
        {
            var normalizedText = NormalizeTicLabelText(label.LabelPlainText);
            var lines = normalizedText.Split('\n');
            var layout = MeasureTicLabelLayout(font, normalizedText, pad);
            if (!TryCreateTicOverlayPlacement(
                    label,
                    layout,
                    lines.Length,
                    plotRect,
                    toPixelX,
                    toPixelY,
                    peakLabelRotateDegrees,
                    (float)uiScale,
                    out var placement))
            {
                continue;
            }

            if (IntersectsAnyRotated(placement, occupied, collisionPad, cos, sin))
            {
                continue;
            }

            occupied.Add(placement);
            result.Add(new TicOverlayDrawItem(label, layout, placement, lines));
        }

        return result;
    }

    private static List<ChromatogramPeakLabel> CollectValidTicOverlayLabels(IReadOnlyList<ChromatogramPeakLabel> labels)
    {
        var valid = new List<ChromatogramPeakLabel>(labels.Count);
        foreach (var label in labels)
        {
            if (string.IsNullOrWhiteSpace(label.LabelPlainText)
                || !double.IsFinite(label.X)
                || !double.IsFinite(label.Y))
            {
                continue;
            }

            valid.Add(label);
        }

        valid.Sort(static (left, right) =>
        {
            var compare = right.Y.CompareTo(left.Y);
            return compare != 0 ? compare : right.X.CompareTo(left.X);
        });
        return valid;
    }

    private static IReadOnlyList<TicLabelPlacement> GetTicOverlayPlacements(
        SKRect plotRect,
        IReadOnlyList<ChromatogramPeakLabel> labels,
        double uiScale,
        double fontSizeDip,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        int peakLabelRotateDegrees,
        TicOverlayPlacementCache? ticOverlayCache)
    {
        if (ticOverlayCache is not null)
        {
            return ticOverlayCache.GetPlacements(
                plotRect,
                labels,
                uiScale,
                fontSizeDip,
                toPixelX,
                toPixelY,
                peakLabelRotateDegrees);
        }

        return BuildTicOverlayDrawItems(
            plotRect,
            labels,
            uiScale,
            fontSizeDip,
            toPixelX,
            toPixelY,
            peakLabelRotateDegrees).ConvertAll(static item => item.Placement);
    }

    /// <summary>
    /// Computes label placements once and returns the pixel-Y gap each vertical marker at
    /// <paramref name="markerPixelXs"/> should leave for overlay labels.
    /// </summary>
    public static VerticalMarkerLabelGap[] GetTicOverlayVerticalMarkerLabelGapsBatch(
        SKRect plotRect,
        IReadOnlyList<ChromatogramPeakLabel> labels,
        double uiScale,
        double fontSizeDip,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        int peakLabelRotateDegrees,
        IReadOnlyList<float> markerPixelXs,
        TicOverlayPlacementCache? ticOverlayCache = null)
    {
        var gaps = new VerticalMarkerLabelGap[markerPixelXs.Count];
        for (var i = 0; i < gaps.Length; i++)
        {
            gaps[i] = VerticalMarkerLabelGap.None;
        }

        if (plotRect.Width <= 1f || plotRect.Height <= 1f || labels.Count == 0 || markerPixelXs.Count == 0)
        {
            return gaps;
        }

        var pad = 4f * (float)uiScale;
        var placements = GetTicOverlayPlacements(
            plotRect,
            labels,
            uiScale,
            fontSizeDip,
            toPixelX,
            toPixelY,
            peakLabelRotateDegrees,
            ticOverlayCache);

        foreach (var placement in placements)
        {
            for (var i = 0; i < markerPixelXs.Count; i++)
            {
                if (!TryGetMinScreenYOnVerticalSlice(placement, markerPixelXs[i], pad, out var sliceTop)
                    || !TryGetMaxScreenYOnVerticalSlice(placement, markerPixelXs[i], pad, out var sliceBottom))
                {
                    continue;
                }

                gaps[i] = MergeVerticalMarkerLabelGaps(
                    gaps[i],
                    new VerticalMarkerLabelGap(sliceTop - pad, sliceBottom + pad));
            }
        }

        return gaps;
    }

    private static bool TryGetTicOverlayLabelPixelExtents(
        SKRect plotRect,
        IReadOnlyList<ChromatogramPeakLabel> labels,
        double uiScale,
        double fontSizeDip,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        int peakLabelRotateDegrees,
        out float minTop,
        out float maxBottom)
    {
        return TryGetTicOverlayLabelPixelExtentsUncollided(
            plotRect,
            labels,
            uiScale,
            fontSizeDip,
            toPixelX,
            toPixelY,
            peakLabelRotateDegrees,
            out minTop,
            out maxBottom);
    }

    /// <summary>
    /// O(n) headroom estimate: each label at its anchor without collision resolution.
    /// Safe for viewport expansion (may reserve slightly more Y than strictly necessary).
    /// </summary>
    private static bool TryGetTicOverlayLabelPixelExtentsUncollided(
        SKRect plotRect,
        IReadOnlyList<ChromatogramPeakLabel> labels,
        double uiScale,
        double fontSizeDip,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        int peakLabelRotateDegrees,
        out float minTop,
        out float maxBottom)
    {
        minTop = float.MaxValue;
        maxBottom = float.MinValue;
        if (plotRect.Width <= 1f || plotRect.Height <= 1f || labels.Count == 0)
        {
            return false;
        }

        var fontPx = (float)Math.Clamp(fontSizeDip, 6d, 24d) * (float)uiScale;
        var font = GetTicOverlayFont(fontPx);
        var pad = 2f * (float)uiScale;
        var uiScaleF = (float)uiScale;
        var found = false;

        foreach (var label in labels)
        {
            if (string.IsNullOrWhiteSpace(label.LabelPlainText)
                || !double.IsFinite(label.X)
                || !double.IsFinite(label.Y))
            {
                continue;
            }

            var normalizedText = NormalizeTicLabelText(label.LabelPlainText);
            var layout = MeasureTicLabelLayout(font, normalizedText, pad);
            if (!TryCreateTicOverlayPlacement(
                    label,
                    layout,
                    0,
                    plotRect,
                    toPixelX,
                    toPixelY,
                    peakLabelRotateDegrees,
                    uiScaleF,
                    out var placement))
            {
                continue;
            }

            minTop = Math.Min(minTop, placement.ScreenTop);
            maxBottom = Math.Max(maxBottom, placement.ScreenBottom);
            found = true;
        }

        return found;
    }

    private static bool TryGetTicOverlayMarkerTopAt(
        float markerPixelX,
        SKRect plotRect,
        IReadOnlyList<ChromatogramPeakLabel> labels,
        double uiScale,
        double fontSizeDip,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        int peakLabelRotateDegrees,
        float pad,
        out float topPixelY)
    {
        topPixelY = plotRect.Top;
        if (!TryGetTicOverlayMarkerLabelGapAt(
                markerPixelX,
                plotRect,
                labels,
                uiScale,
                fontSizeDip,
                toPixelX,
                toPixelY,
                peakLabelRotateDegrees,
                pad,
                out var gap))
        {
            return false;
        }

        topPixelY = gap.GapBottomY;
        return topPixelY < plotRect.Bottom;
    }

    private static bool TryGetTicOverlayMarkerLabelGapAt(
        float markerPixelX,
        SKRect plotRect,
        IReadOnlyList<ChromatogramPeakLabel> labels,
        double uiScale,
        double fontSizeDip,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        int peakLabelRotateDegrees,
        float pad,
        out VerticalMarkerLabelGap gap)
    {
        gap = VerticalMarkerLabelGap.None;
        var placements = GetTicOverlayPlacements(
            plotRect, labels, uiScale, fontSizeDip, toPixelX, toPixelY, peakLabelRotateDegrees, null);
        if (placements.Count == 0)
        {
            return false;
        }

        var found = false;
        foreach (var placement in placements)
        {
            if (!TryGetMinScreenYOnVerticalSlice(placement, markerPixelX, pad, out var sliceTop)
                || !TryGetMaxScreenYOnVerticalSlice(placement, markerPixelX, pad, out var sliceBottom))
            {
                continue;
            }

            gap = MergeVerticalMarkerLabelGaps(
                gap,
                new VerticalMarkerLabelGap(sliceTop - pad, sliceBottom + pad));
            found = true;
        }

        return found && gap.HasGap;
    }

    private static bool TryGetConnectorMarkerTopAt(
        float markerPixelX,
        SKRect plotRect,
        IReadOnlyList<ChromatogramPeakLabel> labels,
        double uiScale,
        double fontSizeDip,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        float pad,
        out float topPixelY)
    {
        topPixelY = plotRect.Top;
        if (!TryGetConnectorMarkerLabelGapAt(
                markerPixelX,
                plotRect,
                labels,
                uiScale,
                fontSizeDip,
                toPixelX,
                toPixelY,
                pad,
                out var gap))
        {
            return false;
        }

        topPixelY = gap.GapBottomY;
        return topPixelY < plotRect.Bottom;
    }

    private static bool TryGetConnectorMarkerLabelGapAt(
        float markerPixelX,
        SKRect plotRect,
        IReadOnlyList<ChromatogramPeakLabel> labels,
        double uiScale,
        double fontSizeDip,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        float pad,
        out VerticalMarkerLabelGap gap)
    {
        gap = VerticalMarkerLabelGap.None;
        if (!TryGetConnectorLabelPixelExtents(
                plotRect,
                labels,
                uiScale,
                fontSizeDip,
                toPixelX,
                toPixelY,
                out _,
                out _))
        {
            return false;
        }

        var fontPx = (float)Math.Clamp(fontSizeDip, 6d, 24d) * (float)uiScale;
        using var typeface = SKTypeface.FromFamilyName(null);
        using var font = new SKFont(typeface, fontPx);
        var lineHeight = fontPx * 1.25f;
        var labelPad = 4f * (float)uiScale;
        var margin = 6f * (float)uiScale;
        var bounds = SKRect.Create(
            plotRect.Left + margin,
            plotRect.Top + margin,
            Math.Max(0, plotRect.Width - 2f * margin),
            Math.Max(0, plotRect.Height - 2f * margin));
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        var occupied = new List<SKRect>();
        var markerRadius = 6f * (float)uiScale;
        var standoff = markerRadius + 12f * (float)uiScale;
        var found = false;
        foreach (var label in labels)
        {
            var text = label.LabelPlainText?.Trim() ?? string.Empty;
            if (text.Length == 0 || !double.IsFinite(label.X) || !double.IsFinite(label.Y))
            {
                continue;
            }

            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            float blockW = 0f;
            foreach (var line in lines)
            {
                blockW = Math.Max(blockW, font.MeasureText(line));
            }

            var blockH = lines.Length * lineHeight;
            var wBox = blockW + 2f * labelPad;
            var hBox = blockH + 2f * labelPad;
            var ax = toPixelX(label.X, plotRect);
            var ay = toPixelY(label.Y, plotRect);
            if (!TryPlaceLabelRect(ax, ay, wBox, hBox, standoff, bounds, occupied, out var rect))
            {
                continue;
            }

            occupied.Add(rect);
            if (markerPixelX < rect.Left - pad || markerPixelX > rect.Right + pad)
            {
                continue;
            }

            gap = MergeVerticalMarkerLabelGaps(
                gap,
                new VerticalMarkerLabelGap(rect.Top - pad, rect.Bottom + pad));
            found = true;
        }

        return found && gap.HasGap;
    }

    private static VerticalMarkerLabelGap MergeVerticalMarkerLabelGaps(
        VerticalMarkerLabelGap existing,
        VerticalMarkerLabelGap candidate)
    {
        if (!existing.HasGap)
        {
            return candidate;
        }

        if (!candidate.HasGap)
        {
            return existing;
        }

        return new VerticalMarkerLabelGap(
            Math.Min(existing.GapTopY, candidate.GapTopY),
            Math.Max(existing.GapBottomY, candidate.GapBottomY));
    }

    private static bool TryGetConnectorLabelPixelExtents(
        SKRect plotRect,
        IReadOnlyList<ChromatogramPeakLabel> labels,
        double uiScale,
        double fontSizeDip,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        out float minTop,
        out float maxBottom)
    {
        minTop = float.MaxValue;
        maxBottom = float.MinValue;
        var fontPx = (float)Math.Clamp(fontSizeDip, 6d, 24d) * (float)uiScale;
        using var typeface = SKTypeface.FromFamilyName(null);
        using var font = new SKFont(typeface, fontPx);
        var lineHeight = fontPx * 1.25f;
        var pad = 4f * (float)uiScale;
        var margin = 6f * (float)uiScale;
        var bounds = SKRect.Create(
            plotRect.Left + margin,
            plotRect.Top + margin,
            Math.Max(0, plotRect.Width - 2f * margin),
            Math.Max(0, plotRect.Height - 2f * margin));
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        var occupied = new List<SKRect>();
        var markerRadius = 6f * (float)uiScale;
        var standoff = markerRadius + 12f * (float)uiScale;
        var found = false;
        foreach (var label in labels)
        {
            var text = label.LabelPlainText?.Trim() ?? string.Empty;
            if (text.Length == 0 || !double.IsFinite(label.X) || !double.IsFinite(label.Y))
            {
                continue;
            }

            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            float blockW = 0f;
            foreach (var line in lines)
            {
                blockW = Math.Max(blockW, font.MeasureText(line));
            }

            var blockH = lines.Length * lineHeight;
            var wBox = blockW + 2f * pad;
            var hBox = blockH + 2f * pad;
            var ax = toPixelX(label.X, plotRect);
            var ay = toPixelY(label.Y, plotRect);
            if (!TryPlaceLabelRect(ax, ay, wBox, hBox, standoff, bounds, occupied, out var rect))
            {
                continue;
            }

            occupied.Add(rect);
            minTop = Math.Min(minTop, rect.Top);
            maxBottom = Math.Max(maxBottom, rect.Bottom);
            found = true;
        }

        return found;
    }
}
