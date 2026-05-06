namespace Griddo.Fields;

public sealed class GriddoCellPropertyView
{
    public string FormatString { get; init; } = string.Empty;
    public string FontFamilyName { get; init; } = string.Empty;
    public double FontSize { get; init; }
    public string FontStyleName { get; init; } = string.Empty;
    public string ForegroundColor { get; init; } = string.Empty;
    public string BackgroundColor { get; init; } = string.Empty;
    public bool NoWrap { get; init; }
}
