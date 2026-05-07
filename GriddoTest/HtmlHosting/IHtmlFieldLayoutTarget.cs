using System.Collections.Generic;
using GriddoModelView;

namespace GriddoTest.HtmlHosting;

public interface IHtmlFieldLayoutTarget
{
    bool IsCategoryField { get; set; }
    string FontFamilyName { get; set; }
    double FontSize { get; set; }
    string FontStyleName { get; set; }
    List<HtmlFieldSegmentConfiguration> Segments { get; set; }
}
