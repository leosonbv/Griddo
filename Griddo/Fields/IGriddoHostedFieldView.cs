using System.Windows;
using System.Windows.Input;
using Griddo.Grid;

namespace Griddo.Fields;

/// <summary>
/// Field that hosts a live <see cref="FrameworkElement"/> per visible cell instead of painting text.
/// </summary>
public interface IGriddoHostedFieldView : IGriddoFieldView
{
    FrameworkElement CreateHostElement();

    void UpdateHostElement(FrameworkElement host, object recordSource, bool isSelected, bool isCurrentCell);

    bool IsHostInEditMode(FrameworkElement host);

    void SetHostEditMode(FrameworkElement host, bool isEditing);

    /// <summary>
    /// When the pointer is over this host, optionally handle wheel zoom so the grid does not scroll.
    /// Default: do not handle.
    /// </summary>
    bool TryHandleHostedMouseWheel(FrameworkElement host, MouseWheelEventArgs e) => false;

    /// <summary>
    /// Optional HTML fragment for the cell when copying the grid as CF_HTML (e.g. inline SVG).
    /// <paramref name="cellWidthPx"/> / <paramref name="cellHeightPx"/> are layout sizes in DIP for raster/SVG dimensions.
    /// </summary>
    bool TryGetClipboardHtmlFragment(
        FrameworkElement? host,
        object recordSource,
        int cellWidthPx,
        int cellHeightPx,
        out string htmlFragment);

    /// <summary>Sync chart stroke/font scale with grid <see cref="Griddo.ContentScale"/> (Ctrl+wheel).</summary>
    void SyncHostedUiScale(FrameworkElement host, double contentScale);

    /// <summary>
    /// When <see cref="Griddo.HostedPlotDirectEditOnMouseDown"/> changes, update hosted Plotto flags (e.g. defer renderer activation).
    /// Default: no-op.
    /// </summary>
    void ApplyPlotDirectEditOption(FrameworkElement host, bool gridUsesHostedPlotDirectMouseDown)
    {
    }

    /// <summary>
    /// After the grid enables hosted edit mode on mouse down, forward the activating click to the chart so editor gestures run on the same press.
    /// Default: no-op.
    /// </summary>
    void RelayDirectEditMouseDown(FrameworkElement host, MouseButtonEventArgs eFromGrid)
    {
    }

    /// <summary>
    /// Optional companion for <see cref="RelayDirectEditMouseDown"/> when hosted editors need mouse-up state transitions
    /// (e.g. delayed context-menu opening after right-button release).
    /// Default: no-op.
    /// </summary>
    void RelayDirectEditMouseUp(FrameworkElement host, MouseButtonEventArgs eFromGrid)
    {
    }
}
