namespace Plotto.Charting.Core;

public readonly record struct IntegrationRegion(ChartPoint Start, ChartPoint End);

public readonly record struct ColoredIntegrationRegion(
    IntegrationRegion Region,
    byte R,
    byte G,
    byte B,
    byte A,
    IReadOnlyList<ChartPoint>? ShapePoints = null);
