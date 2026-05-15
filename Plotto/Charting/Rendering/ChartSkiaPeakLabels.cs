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
        SKPaint? connectorPaint = null)
    {
        if (plotRect.Width <= 1f || plotRect.Height <= 1f || labels.Count == 0)
        {
            return;
        }

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
}
