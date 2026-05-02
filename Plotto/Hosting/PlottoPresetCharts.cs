using System.Collections.Generic;
using System.Windows;
using Plotto.Charting.Controls;
using Plotto.Charting.Core;

namespace Plotto;

/// <summary>
/// Shared singleton chart instances and demo data used when Plotto is hosted in grid cells (chromatogram editor,
/// calibration editor). Keeps the namespace name separate from this type (avoid <c>Plotto.Plotto</c> confusion).
/// </summary>
public static class PlottoPresetCharts
{
    public static ChromatogramControl Editor { get; } = CreateEditor();

    /// <summary>Shared calibration-curve editor for hosted grid cells (same attach pattern as <see cref="Editor"/>).</summary>
    public static CalibrationCurveControl CalibrationEditor { get; } = CreateCalibrationEditor();

    private static ChromatogramControl CreateEditor()
    {
        return new ChromatogramControl
        {
            RequireActivationClick = true,
            RenderMode = ChartRenderMode.Renderer,
            EnableInlineEditing = true,
            EnableMouseInteractions = true,
            ChartTitle = "Chromatogram",
            AxisLabelX = "Time",
            AxisLabelY = "Intensity",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
    }

    private static CalibrationCurveControl CreateCalibrationEditor()
    {
        return new CalibrationCurveControl
        {
            RequireActivationClick = true,
            RenderMode = ChartRenderMode.Renderer,
            EnableInlineEditing = true,
            EnableMouseInteractions = true,
            ChartTitle = "Calibration curve",
            AxisLabelX = "Concentration",
            AxisLabelY = "Response",
            FitMode = CalibrationFitMode.Linear,
            CalibrationPoints = DemoCalibrationPoints,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
    }

    /// <summary>Five example standards (concentration × response).</summary>
    public static IReadOnlyList<CalibrationPoint> DemoCalibrationPoints { get; } =
        new[]
        {
            new CalibrationPoint { X = 0.5, Y = 1.05 },
            new CalibrationPoint { X = 1.0, Y = 2.35 },
            new CalibrationPoint { X = 2.0, Y = 5.10 },
            new CalibrationPoint { X = 4.0, Y = 11.60 },
            new CalibrationPoint { X = 8.0, Y = 24.20 }
        };
}
