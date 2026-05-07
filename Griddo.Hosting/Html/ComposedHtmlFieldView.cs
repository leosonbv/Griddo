using System.Globalization;
using System.Net;
using System.Text;
using System.Windows;
using Griddo.Editing;
using Griddo.Fields;
using Griddo.Hosting.Configuration;

namespace Griddo.Hosting.Html;

public sealed class ComposedHtmlFieldView : IGriddoFieldView, IGriddoFieldDescriptionView, IGriddoFieldSourceMember, IGriddoFieldSourceObject, IGriddoFieldTitleView, IGriddoFieldFontView, IGriddoFieldWrapView, IGriddoFieldSortValueView, IGriddoRecordMergeBandView, IHtmlFieldLayoutTarget
{
    private readonly Func<IReadOnlyList<IGriddoFieldView>> _allFieldsAccessor;

    public ComposedHtmlFieldView(
        string header,
        double width,
        Func<IReadOnlyList<IGriddoFieldView>> allFieldsAccessor,
        string sourceObjectName = "",
        string sourceMemberName = "")
    {
        Header = header;
        Width = width;
        _allFieldsAccessor = allFieldsAccessor;
        SourceObjectName = sourceObjectName;
        SourceMemberName = sourceMemberName;
    }

    public string Header { get; set; }
    public string AbbreviatedHeader { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SourceFieldIndex { get; set; } = -1;
    public string SourceMemberName { get; }
    public string SourceObjectName { get; }
    public double Width { get; }
    public bool Fill { get; set; }
    public bool IsHtml => true;
    public TextAlignment ContentAlignment => TextAlignment.Left;
    public IGriddoCellEditor Editor => GriddoCellEditors.Text;
    public string FontFamilyName { get; set; } = string.Empty;
    public double FontSize { get; set; }
    public string FontStyleName { get; set; } = string.Empty;
    public bool NoWrap { get; set; }
    public bool IsCategoryField { get; set; }
    public List<HtmlFieldSegmentConfiguration> Segments { get; set; } = [];

    public object? GetSortValue(object recordSource) => GetValue(recordSource)?.ToString() ?? string.Empty;
    public bool IsMergedWithPreviousRecord(IReadOnlyList<object> records, int recordIndex) => false;
    public bool IsMergedWithNextRecord(IReadOnlyList<object> records, int recordIndex) => false;

    public object? GetValue(object recordSource)
    {
        var fields = _allFieldsAccessor();
        var enabled = Segments.Where(static s => s.Enabled).ToList();
        if (enabled.Count == 0)
        {
            return string.Empty;
        }

        var rows = new List<string>(enabled.Count);
        foreach (var segment in enabled)
        {
            if (segment.SourceFieldIndex < 0 || segment.SourceFieldIndex >= fields.Count)
            {
                continue;
            }

            var field = fields[segment.SourceFieldIndex];
            if (ReferenceEquals(field, this))
            {
                continue;
            }

            var header = string.IsNullOrWhiteSpace(segment.AbbreviatedHeaderOverride)
                ? (field is IGriddoFieldTitleView t && !string.IsNullOrWhiteSpace(t.AbbreviatedHeader) ? t.AbbreviatedHeader : field.Header)
                : segment.AbbreviatedHeaderOverride;
            var value = field.GetValue(recordSource);
            var rendered = field.IsHtml
                ? (value?.ToString() ?? string.Empty)
                : WebUtility.HtmlEncode(field.FormatValue(value));

            if (!segment.WordWrap)
            {
                rendered = rendered.Replace(" ", "\u00A0", StringComparison.Ordinal);
            }

            var breakBefore = segment.AddLineBreakAfter ? " data-break-before='1'" : string.Empty;
            rows.Add($"<tr{breakBefore}><td><b>{WebUtility.HtmlEncode(header)}</b></td><td>{rendered}</td></tr>");
        }

        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var style = BuildSelfStyle();
        return $"<table style='border-collapse:collapse;border:none;{style}'><tbody>{string.Join(string.Empty, rows)}</tbody></table>";
    }

    public bool TrySetValue(object recordSource, object? value) => false;
    public string FormatValue(object? value) => value?.ToString() ?? string.Empty;

    private string BuildSelfStyle()
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(FontFamilyName))
        {
            sb.Append("font-family:").Append(WebUtility.HtmlEncode(FontFamilyName)).Append(';');
        }

        if (FontSize > 0)
        {
            sb.Append("font-size:").Append(FontSize.ToString(CultureInfo.InvariantCulture)).Append("px;");
        }

        if (!string.IsNullOrWhiteSpace(FontStyleName))
        {
            sb.Append("font-style:").Append(WebUtility.HtmlEncode(FontStyleName)).Append(';');
        }

        return sb.ToString();
    }
}
