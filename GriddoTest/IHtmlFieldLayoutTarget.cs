using System.Collections.Generic;
using GriddoModelView;

namespace GriddoTest;

public interface IHtmlFieldLayoutTarget
{
    string FontFamilyName { get; set; }
    double FontSize { get; set; }
    string FontStyleName { get; set; }
    List<HtmlFieldSegmentConfiguration> Segments { get; set; }
}
