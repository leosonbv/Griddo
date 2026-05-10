namespace Plotto.Charting.Core;

public sealed class CalibrationPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>Flattened label lines drawn near the marker (from dose name and/or HTML segment config).</summary>
    public string LabelPlainText { get; set; } = string.Empty;
}
