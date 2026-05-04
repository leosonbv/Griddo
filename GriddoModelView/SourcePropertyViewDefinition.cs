namespace GriddoModelView;

public sealed class SourcePropertyViewDefinition
{
    public string SourceClassName { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
    public string Header { get; set; } = string.Empty;
    public string AbbreviatedHeader { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string StringFormat { get; set; } = string.Empty;
    public double FontSize { get; set; }
    public string FontStyle { get; set; } = string.Empty;
    public string ForegroundColor { get; set; } = string.Empty;
    public string BackgroundColor { get; set; } = string.Empty;
}
