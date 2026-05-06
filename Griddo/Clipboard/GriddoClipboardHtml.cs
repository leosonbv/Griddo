using System.Globalization;
using System.Net;
using System.Text;

namespace Griddo.Clipboard;

/// <summary>Windows CF_HTML clipboard wrapper for rich paste into Word, Excel, browsers.</summary>
internal static class GriddoClipboardHtml
{
    /// <summary>UTF-8 CF_HTML with fragment markers (offsets are byte indices from start of the returned string when encoded as UTF-8).</summary>
    public static string EncodeHtmlFragment(string utf8InnerHtml)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        const string docPrefix =
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body><!--StartFragment-->";
        const string docSuffix = "<!--EndFragment--></body></html>";

        var htmlDocument = docPrefix + utf8InnerHtml + docSuffix;
        var documentBytes = utf8.GetBytes(htmlDocument);

        var startFragMarker = utf8.GetBytes("<!--StartFragment-->");
        var endFragMarker = utf8.GetBytes("<!--EndFragment-->");

        var idxStartMarker = IndexOf(documentBytes, startFragMarker);
        var idxEndMarker = IndexOf(documentBytes, endFragMarker);
        if (idxStartMarker < 0 || idxEndMarker < 0)
        {
            throw new InvalidOperationException("CF_HTML document missing fragment markers.");
        }

        var startFragmentContent = idxStartMarker + startFragMarker.Length;
        var endFragmentContent = idxEndMarker;

        var headerSizeBytes = 0;
        for (var iter = 0; iter < 24; iter++)
        {
            var startHtml = headerSizeBytes;
            var endHtml = headerSizeBytes + documentBytes.Length;
            var startFragment = headerSizeBytes + startFragmentContent;
            var endFragment = headerSizeBytes + endFragmentContent;

            var inv = CultureInfo.InvariantCulture;
            var headerText =
                "Version:1.0\r\n" +
                $"StartHTML:{startHtml.ToString("D10", inv)}\r\n" +
                $"EndHTML:{endHtml.ToString("D10", inv)}\r\n" +
                $"StartFragment:{startFragment.ToString("D10", inv)}\r\n" +
                $"EndFragment:{endFragment.ToString("D10", inv)}\r\n";

            var newHeaderSize = utf8.GetByteCount(headerText);
            if (newHeaderSize == headerSizeBytes)
            {
                return headerText + htmlDocument;
            }

            headerSizeBytes = newHeaderSize;
        }

        throw new InvalidOperationException("CF_HTML header length did not converge.");
    }

    private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var ok = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
            {
                return i;
            }
        }

        return -1;
    }

    public static string EscapeCellText(string plain)
    {
        return WebUtility.HtmlEncode(plain);
    }
}
