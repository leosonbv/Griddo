using System.IO;
using System.Windows;
using SkiaSharp;

namespace Plotto.Charting.Controls;

public abstract partial class SkiaChartBaseControl
{
    /// <summary>Renders the current chart to SVG markup (same drawing path as on-screen).</summary>
    public string GetChartSvgMarkup(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        using var stream = new MemoryStream();
        using (var canvas = SKSvgCanvas.Create(new SKRect(0, 0, width, height), stream))
        {
            DrawChart(canvas, width, height);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Raster snapshot for clipboard HTML (Excel-friendly vs inline SVG).</summary>
    public byte[] GetChartPngBytes(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        DrawChart(surface.Canvas, width, height);
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    /// <summary>Removes the XML prologue so SVG can be inlined in HTML.</summary>
    public static string TrimSvgXmlDeclaration(string svg)
    {
        if (string.IsNullOrEmpty(svg))
        {
            return svg;
        }

        var s = svg.AsSpan().TrimStart();
        if (s.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
        {
            var idx = svg.IndexOf("?>", StringComparison.Ordinal);
            if (idx >= 0)
            {
                return svg[(idx + 2)..].TrimStart();
            }
        }

        return svg;
    }

    public void CopyAsSvgToClipboard()
    {
        var width = (int)Math.Max(1, ActualWidth);
        var height = (int)Math.Max(1, ActualHeight);
        var svg = GetChartSvgMarkup(width, height);
        var dataObject = new DataObject();
        dataObject.SetData("image/svg+xml", svg);
        dataObject.SetData(DataFormats.UnicodeText, svg);
        Clipboard.SetDataObject(dataObject, true);
    }
}
