using Plotto.Charting.Core;
using SkiaSharp;

namespace Plotto.Charting.Rendering;

/// <summary>Draws plain-text peak labels near integration anchors with simple collision avoidance.</summary>
internal static class ChartSkiaPeakLabels
{
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
        bool showDebugRect = false)
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
                showDebugRect);
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
        bool showDebugRect)
    {
        var fontPx = (float)Math.Clamp(fontSizeDip, 6d, 24d) * (float)uiScale;
        using var typeface = SKTypeface.FromFamilyName(null);
        using var font = new SKFont(typeface, fontPx);
        var pad = 2f * (float)uiScale;
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

        using var missingPeakPaint = new SKPaint
        {
            IsAntialias = textPaint.IsAntialias,
            Color = new SKColor(80, 140, 255),
            Style = textPaint.Style
        };

        var occupied = new List<TicLabelPlacement>();
        var ordered = labels
            .Where(static l => !string.IsNullOrWhiteSpace(l.LabelPlainText)
                               && double.IsFinite(l.X)
                               && double.IsFinite(l.Y))
            .OrderByDescending(static l => l.Y)
            .ThenByDescending(static l => l.X)
            .ToList();

        foreach (var label in ordered)
        {
            var text = label.LabelPlainText.Trim();
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var layout = MeasureTicLabelLayout(font, lines, pad);
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

            if (IntersectsAnyRotated(placement, occupied, 2f))
            {
                continue;
            }

            occupied.Add(placement);
            canvas.Save();
            canvas.Translate(placement.AnchorX, placement.AnchorY);
            canvas.RotateDegrees(peakLabelRotateDegrees);
            canvas.Translate(-placement.AnchorLocal.X, -placement.AnchorLocal.Y);

            if (showDebugRect)
            {
                DrawTicOverlayDebugRect(canvas, placement, (float)uiScale);
            }

            var paint = label.PeakFound ? textPaint : missingPeakPaint;
            var baseline = layout.FirstBaseline;
            var centerX = layout.Width * 0.5f;
            foreach (var line in lines)
            {
                canvas.DrawText(line, centerX, baseline, SKTextAlign.Center, font, paint);
                baseline += layout.LineStep;
            }

            canvas.Restore();
        }
    }

    private readonly record struct TicLabelPlacement(
        float AnchorX,
        float AnchorY,
        float Width,
        float TopLocalY,
        float BottomLocalY,
        float Degrees,
        SKPoint AnchorLocal);

    private readonly record struct TicLabelLayout(
        float Width,
        float TopLocalY,
        float BottomLocalY,
        float FirstBaseline,
        float LineStep,
        float LastLineBottom = 0f,
        float TextAscent = 0f,
        float TextDescent = 0f);

    private static TicLabelLayout MeasureTicLabelLayout(SKFont font, string[] lines, float pad)
    {
        font.GetFontMetrics(out var metrics);
        var ascent = -metrics.Ascent;
        var descent = metrics.Descent;
        var lineStep = ascent + descent;
        if (lineStep <= 0f)
        {
            lineStep = font.Size;
        }

        var padX = pad;
        var padY = pad * 0.85f;

        float blockW = 0f;
        foreach (var line in lines)
        {
            blockW = Math.Max(blockW, font.MeasureText(line));
        }

        var lastBaseline = -padY - descent;
        var firstBaseline = lastBaseline - (lines.Length - 1) * lineStep;
        var baseline = firstBaseline;
        var blockTop = float.MaxValue;
        var blockBottom = float.MinValue;
        for (var i = 0; i < lines.Length; i++)
        {
            blockTop = Math.Min(blockTop, baseline - ascent);
            blockBottom = Math.Max(blockBottom, baseline + descent);
            baseline += lineStep;
        }

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

        // Find the 2 corners with highest screen Y (lowest on screen).
        // After rotation, bottom-left and bottom-right are the original bottom corners.
        var corners = new[] { (bl_screenX, bl_screenY), (br_screenX, br_screenY), (tr_screenX, tr_screenY), (tl_screenX, tl_screenY) };
        System.Array.Sort(corners, (a, b) => b.Item2.CompareTo(a.Item2));

        // Take exactly the 2 with highest Y values (the 2 lowest corners on screen).
        var lowestY = corners[0].Item2;
        var lowest = new List<(float screenX, float screenY)>();
        for (var i = 0; i < corners.Length; i++)
        {
            if (corners[i].Item2 >= lowestY - 0.05f)  // Allow small tolerance for floating point
            {
                lowest.Add(corners[i]);
            }
            if (lowest.Count >= 2)
            {
                break;
            }
        }

        if (lowest.Count < 2)
        {
            // Fallback: just take the first 2 sorted corners
            lowest = new List<(float screenX, float screenY)> { corners[0], corners[1] };
        }

        // Average screen offset of the 2 lowest corners.
        var sumScreenX = 0f;
        var sumScreenY = 0f;
        foreach (var c in lowest)
        {
            sumScreenX += c.screenX;
            sumScreenY += c.screenY;
        }

        var avgScreenX = sumScreenX / lowest.Count;
        var avgScreenY = sumScreenY / lowest.Count;

        // Position the anchor so that:
        // - The average X of the 2 lowest rotated corners is aligned with the apex X.
        // - The average Y of the 2 lowest rotated corners is at apexPixelY - gap.
        var anchorX = apexPixelX - avgScreenX;
        var anchorY = apexPixelY - gap - avgScreenY;

        placement = new TicLabelPlacement(
            anchorX,
            anchorY,
            w,
            top,
            bot,
            peakLabelRotateDegrees,
            new SKPoint(anchorLocalX, anchorLocalY));
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

    private static bool IntersectsAnyRotated(
        TicLabelPlacement candidate,
        IReadOnlyList<TicLabelPlacement> occupied,
        float pad)
    {
        foreach (var other in occupied)
        {
            if (RotatedRectsOverlap(candidate, other, pad))
            {
                return true;
            }
        }

        return false;
    }

    private static bool RotatedRectsOverlap(TicLabelPlacement left, TicLabelPlacement right, float pad)
    {
        Span<SKPoint> leftCorners = stackalloc SKPoint[4];
        Span<SKPoint> rightCorners = stackalloc SKPoint[4];
        GetScreenCorners(left, leftCorners);
        GetScreenCorners(right, rightCorners);
        return PolygonsOverlap(leftCorners, rightCorners, pad);
    }

    private static void GetScreenCorners(TicLabelPlacement placement, Span<SKPoint> corners)
    {
        ReadOnlySpan<SKPoint> localCorners =
        [
            new(0f, placement.BottomLocalY),
            new(placement.Width, placement.BottomLocalY),
            new(placement.Width, placement.TopLocalY),
            new(0f, placement.TopLocalY)
        ];

        var rad = placement.Degrees * MathF.PI / 180f;
        var cos = MathF.Cos(rad);
        var sin = MathF.Sin(rad);
        for (var i = 0; i < localCorners.Length; i++)
        {
            var localX = localCorners[i].X - placement.AnchorLocal.X;
            var localY = localCorners[i].Y - placement.AnchorLocal.Y;
            corners[i] = new SKPoint(
                placement.AnchorX + RotateX(localX, localY, cos, sin),
                placement.AnchorY + RotateY(localX, localY, cos, sin));
        }
    }

    private static bool PolygonsOverlap(ReadOnlySpan<SKPoint> left, ReadOnlySpan<SKPoint> right, float pad) =>
        !HasSeparatingAxis(left, right, pad) && !HasSeparatingAxis(right, left, pad);

    private static bool HasSeparatingAxis(ReadOnlySpan<SKPoint> from, ReadOnlySpan<SKPoint> other, float pad)
    {
        for (var i = 0; i < from.Length; i++)
        {
            var p1 = from[i];
            var p2 = from[(i + 1) % from.Length];
            var edgeX = p2.X - p1.X;
            var edgeY = p2.Y - p1.Y;
            var axisX = -edgeY;
            var axisY = edgeX;
            var length = MathF.Sqrt(axisX * axisX + axisY * axisY);
            if (length < 1e-6f)
            {
                continue;
            }

            axisX /= length;
            axisY /= length;
            ProjectOntoAxis(from, axisX, axisY, out var minFrom, out var maxFrom);
            ProjectOntoAxis(other, axisX, axisY, out var minOther, out var maxOther);
            if (maxFrom + pad < minOther - pad || maxOther + pad < minFrom - pad)
            {
                return true;
            }
        }

        return false;
    }

    private static void ProjectOntoAxis(
        ReadOnlySpan<SKPoint> points,
        float axisX,
        float axisY,
        out float min,
        out float max)
    {
        min = float.MaxValue;
        max = float.MinValue;
        foreach (var point in points)
        {
            var projection = point.X * axisX + point.Y * axisY;
            min = MathF.Min(min, projection);
            max = MathF.Max(max, projection);
        }
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
        out float minTop)
    {
        minTop = float.MaxValue;
        if (plotRect.Width <= 1f || plotRect.Height <= 1f || labels.Count == 0)
        {
            return false;
        }

        if (ticOverlayMode)
        {
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

    /// <summary>
    /// Builds the collision-resolved TIC overlay placements for <paramref name="labels"/> once.
    /// Returns the list (possibly empty) and the shared font resources so callers can reuse them.
    /// </summary>
    private static List<TicLabelPlacement> TryBuildTicOverlayPlacements(
        SKRect plotRect,
        IReadOnlyList<ChromatogramPeakLabel> labels,
        double uiScale,
        double fontSizeDip,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        int peakLabelRotateDegrees)
    {
        var result = new List<TicLabelPlacement>();
        var fontPx = (float)Math.Clamp(fontSizeDip, 6d, 24d) * (float)uiScale;
        using var typeface = SKTypeface.FromFamilyName(null);
        using var font = new SKFont(typeface, fontPx);
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

        var ordered = labels
            .Where(static l => !string.IsNullOrWhiteSpace(l.LabelPlainText)
                               && double.IsFinite(l.X)
                               && double.IsFinite(l.Y))
            .OrderByDescending(static l => l.Y)
            .ThenByDescending(static l => l.X)
            .ToList();

        foreach (var label in ordered)
        {
            var text = label.LabelPlainText.Trim();
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var layout = MeasureTicLabelLayout(font, lines, pad);
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

            if (IntersectsAnyRotated(placement, result, 2f))
            {
                continue;
            }

            result.Add(placement);
        }

        return result;
    }

    /// <summary>
    /// Computes label placements once and returns the lowest label pixel-Y that each vertical
    /// marker at <paramref name="markerPixelXs"/> would collide with.  Index in the output array
    /// corresponds to index in <paramref name="markerPixelXs"/>; a value of
    /// <see cref="float.NegativeInfinity"/> means no label covers that marker.
    /// </summary>
    public static float[] GetTicOverlayVerticalMarkerCapYBatch(
        SKRect plotRect,
        IReadOnlyList<ChromatogramPeakLabel> labels,
        double uiScale,
        double fontSizeDip,
        Func<double, SKRect, float> toPixelX,
        Func<double, SKRect, float> toPixelY,
        int peakLabelRotateDegrees,
        IReadOnlyList<float> markerPixelXs)
    {
        var capY = new float[markerPixelXs.Count];
        for (var i = 0; i < capY.Length; i++)
        {
            capY[i] = float.NegativeInfinity;
        }

        if (plotRect.Width <= 1f || plotRect.Height <= 1f || labels.Count == 0 || markerPixelXs.Count == 0)
        {
            return capY;
        }

        var pad = 4f * (float)uiScale;
        var placements = TryBuildTicOverlayPlacements(
            plotRect, labels, uiScale, fontSizeDip, toPixelX, toPixelY, peakLabelRotateDegrees);

        foreach (var placement in placements)
        {
            for (var i = 0; i < markerPixelXs.Count; i++)
            {
                if (TryGetMaxScreenYOnVerticalSlice(placement, markerPixelXs[i], pad, out var sliceBottom))
                {
                    var candidate = sliceBottom + pad;
                    if (candidate > capY[i])
                    {
                        capY[i] = candidate;
                    }
                }
            }
        }

        return capY;
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
        minTop = float.MaxValue;
        maxBottom = float.MinValue;
        var placements = TryBuildTicOverlayPlacements(
            plotRect, labels, uiScale, fontSizeDip, toPixelX, toPixelY, peakLabelRotateDegrees);
        if (placements.Count == 0)
        {
            return false;
        }

        foreach (var placement in placements)
        {
            GetScreenBounds(placement, out _, out var top, out _, out var bottom);
            minTop = Math.Min(minTop, top);
            maxBottom = Math.Max(maxBottom, bottom);
        }

        return true;
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
        var placements = TryBuildTicOverlayPlacements(
            plotRect, labels, uiScale, fontSizeDip, toPixelX, toPixelY, peakLabelRotateDegrees);
        if (placements.Count == 0)
        {
            return false;
        }

        var found = false;
        foreach (var placement in placements)
        {
            if (!TryGetMaxScreenYOnVerticalSlice(placement, markerPixelX, pad, out var sliceBottom))
            {
                continue;
            }

            topPixelY = Math.Max(topPixelY, sliceBottom + pad);
            found = true;
        }

        return found && topPixelY < plotRect.Bottom;
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

            topPixelY = Math.Max(topPixelY, rect.Bottom + pad);
            found = true;
        }

        return found && topPixelY < plotRect.Bottom;
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

    private static void GetScreenBounds(TicLabelPlacement placement, out float left, out float top, out float right, out float bottom)
    {
        Span<SKPoint> corners = stackalloc SKPoint[4];
        GetScreenCorners(placement, corners);
        left = float.MaxValue;
        top = float.MaxValue;
        right = float.MinValue;
        bottom = float.MinValue;
        foreach (var corner in corners)
        {
            left = Math.Min(left, corner.X);
            top = Math.Min(top, corner.Y);
            right = Math.Max(right, corner.X);
            bottom = Math.Max(bottom, corner.Y);
        }
    }
}
