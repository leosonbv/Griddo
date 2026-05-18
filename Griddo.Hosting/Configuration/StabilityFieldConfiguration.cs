namespace Griddo.Hosting.Configuration;

public enum StabilityAxisSide
{
    None = 0,
    Left = 1,
    Right = 2,
    Both = 3
}

public sealed class StabilitySeriesConfiguration
{
    public int SourceFieldIndex { get; set; } = -1;
    public bool Enabled { get; set; }
    public bool ShowSdLines { get; set; } = true;
    public bool ShowLine { get; set; }
    public bool ShowMarker { get; set; } = true;
    public string Color { get; set; } = string.Empty;
    public StabilityAxisSide AxisSide { get; set; } = StabilityAxisSide.Left;
}

public sealed class StabilityFieldConfiguration
{
    public int SourceFieldIndex { get; set; }
    public string Label { get; set; } = string.Empty;
    public List<StabilitySeriesConfiguration> Series { get; set; } = [];
}
