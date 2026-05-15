namespace Plotto.Charting.Core;

/// <summary>Peak annotation in plot data coordinates (same X axis as the chromatogram trace).</summary>
public readonly record struct ChromatogramPeakLabel(double X, double Y, string LabelPlainText);
