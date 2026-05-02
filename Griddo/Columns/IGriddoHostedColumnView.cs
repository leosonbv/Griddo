using System.Windows;
using System.Windows.Input;

namespace Griddo;

/// <summary>
/// Column that hosts a live <see cref="FrameworkElement"/> per visible cell instead of painting text.
/// </summary>
public interface IGriddoHostedColumnView : IGriddoColumnView
{
    FrameworkElement CreateHostElement();

    void UpdateHostElement(FrameworkElement host, object rowSource, bool isSelected, bool isCurrentCell);

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
        object rowSource,
        int cellWidthPx,
        int cellHeightPx,
        out string htmlFragment);

    /// <summary>Sync chart stroke/font scale with grid <see cref="Griddo.ContentScale"/> (Ctrl+wheel).</summary>
    void SyncHostedUiScale(FrameworkElement host, double contentScale);
}
