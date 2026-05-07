namespace Griddo.Hosting.Configuration;

public sealed class HtmlFieldSegmentConfiguration
{
    /// <summary>-1 means "resolved via <see cref="SourceFieldKey"/>" only; avoids colliding keyed segments at implicit index 0.</summary>
    public int SourceFieldIndex { get; set; } = -1;
    public string SourceFieldKey { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string AbbreviatedHeaderOverride { get; set; } = string.Empty;
    public bool AddLineBreakAfter { get; set; } = true;
    public bool WordWrap { get; set; } = true;
}

public sealed class HtmlFieldConfiguration
{
    public int SourceFieldIndex { get; set; } = -1;
    public string SourceFieldKey { get; set; } = string.Empty;
    public bool IsTable { get; set; } = true;
    public bool IsCategoryField { get; set; }
    public string FontFamilyName { get; set; } = string.Empty;
    public double FontSize { get; set; }
    public string FontStyleName { get; set; } = string.Empty;
    public List<HtmlFieldSegmentConfiguration> Segments { get; set; } = [];
}
