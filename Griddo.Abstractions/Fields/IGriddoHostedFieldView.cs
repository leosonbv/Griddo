using System.Windows;
using System.Windows.Input;

namespace Griddo.Fields;

public interface IGriddoHostedFieldView : IGriddoFieldView
{
    FrameworkElement CreateHostElement();
    void UpdateHostElement(FrameworkElement host, object recordSource, bool isSelected, bool isCurrentCell);
    bool IsHostInEditMode(FrameworkElement host);
    void SetHostEditMode(FrameworkElement host, bool isEditing);
    bool TryHandleHostedMouseWheel(FrameworkElement host, MouseWheelEventArgs e) => false;
    bool TryGetClipboardHtmlFragment(
        FrameworkElement? host,
        object recordSource,
        int cellWidthPx,
        int cellHeightPx,
        out string htmlFragment);
    void SyncHostedUiScale(FrameworkElement host, double contentScale);
    void ApplyPlotDirectEditOption(FrameworkElement host, bool gridUsesHostedPlotDirectMouseDown)
    {
    }

    /// <summary>
    /// When true, left-button double-clicks are relayed to <see cref="RelayDirectEditMouseDown"/> even while
    /// <see cref="IsHostInEditMode"/> is true (Skia charts that stay in Editor after zoom).
    /// </summary>
    bool ShouldRelayLeftDoubleClickWhileInHostedEditMode() => false;

    void RelayDirectEditMouseDown(FrameworkElement host, MouseButtonEventArgs eFromGrid)
    {
    }

    void RelayDirectEditMouseUp(FrameworkElement host, MouseButtonEventArgs eFromGrid)
    {
    }
}
