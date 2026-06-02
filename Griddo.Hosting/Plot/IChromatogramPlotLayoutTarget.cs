namespace Griddo.Hosting.Plot;

/// <summary>
/// Chromatogram-specific layout options (peak overlays, RT markers, label rotation).
/// Segregated per ISP.
/// </summary>
public interface IChromatogramPlotLayoutTarget
{
    bool ChromatogramShowPeaks { get; set; }
    /// <summary>Chromatogram: dashed vertical line at corrected expected retention time.</summary>
    bool ChromatogramShowExpectedRtLine { get; set; }
    /// <summary>Chromatogram: light dashed lines at ±TimeWindow/2 around corrected expected RT.</summary>
    bool ChromatogramShowRtLimitLines { get; set; }
    /// <summary>Sample TIC: show corrected expected RT lines for compounds in compound selection (and current row).</summary>
    bool ChromatogramShowSelectionCorrectedRtOnTic { get; set; }

    bool OverlayIstdPeaks { get; set; }
    bool OverlaySurrogatePeaks { get; set; }
    bool OverlayTargetPeaks { get; set; }

    /// <summary>Peak label rotation in degrees (0 = horizontal). Used by chromatogram plots with fixed overlay labels.</summary>
    int PeakLabelRotate { get; set; }

    /// <summary>Peak/compound label font size; 0 = default (13).</summary>
    double PeakLabelFontSize { get; set; }
}
