using System.Collections.Generic;

namespace GriddoModelView
{
    public enum HtmlLayoutMode
    {
        Table = 0,
        SingleDiv = 1
    }

    public sealed class HtmlFieldSegmentConfiguration
    {
        public int SourceFieldIndex { get; set; }
        public bool Enabled { get; set; } = true;
        public string AbbreviatedHeaderOverride { get; set; } = string.Empty;
        public bool AddLineBreakAfter { get; set; } = true;
        public bool WordWrap { get; set; } = true;
    }

    public sealed class HtmlFieldConfiguration
    {
        public int SourceFieldIndex { get; set; }
        public HtmlLayoutMode LayoutMode { get; set; } = HtmlLayoutMode.Table;
        public string FontFamilyName { get; set; } = string.Empty;
        public double FontSize { get; set; }
        public string FontStyleName { get; set; } = string.Empty;
        public List<HtmlFieldSegmentConfiguration> Segments { get; set; } = new();
    }
}
