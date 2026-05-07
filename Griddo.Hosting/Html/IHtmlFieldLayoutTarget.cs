using Griddo.Hosting.Configuration;

namespace Griddo.Hosting.Html;

public interface IHtmlFieldLayoutTarget
{
    bool IsCategoryField { get; set; }
    string FontFamilyName { get; set; }
    double FontSize { get; set; }
    string FontStyleName { get; set; }
    List<HtmlFieldSegmentConfiguration> Segments { get; set; }
}
