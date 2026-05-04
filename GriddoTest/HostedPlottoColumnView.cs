using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using Plotto.Charting.Controls;
using Plotto.Charting.Core;
using Griddo;
using Griddo.Columns;
using Griddo.Editing;

namespace GriddoTest;

public sealed class HostedPlottoColumnView : IGriddoHostedColumnView, IGriddoColumnSourceMember, IGriddoColumnSourceObject
{
    private static bool _sharedEditorHooked;
    private readonly Func<object, int> _plottoSeedGetter;

    public HostedPlottoColumnView(
        string header,
        double width,
        Func<object, int> plottoSeedGetter,
        string sourceObjectName = "",
        string sourceMemberName = "")
    {
        Header = header;
        Width = width;
        _plottoSeedGetter = plottoSeedGetter;
        SourceObjectName = sourceObjectName;
        SourceMemberName = sourceMemberName;
        Editor = GriddoCellEditors.Text;
        ContentAlignment = TextAlignment.Left;
    }

    public string Header { get; set; }

    public string SourceObjectName { get; }
    public string SourceMemberName { get; }
    public double Width { get; }
    public bool Fill { get; set; }
    public bool IsHtml => false;
    public TextAlignment ContentAlignment { get; }
    public IGriddoCellEditor Editor { get; }

    public object? GetValue(object rowSource) => null;

    public bool TrySetValue(object rowSource, object? value) => false;

    public string FormatValue(object? value) => string.Empty;

    public FrameworkElement CreateHostElement()
    {
        var chart = new ChromatogramControl
        {
            RequireActivationClick = false,
            RenderMode = ChartRenderMode.Renderer,
            EnableInlineEditing = false,
            EnableMouseInteractions = true,
            ChartTitle = "Chromatogram",
            AxisLabelX = "Time",
            AxisLabelY = "Intensity",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = true
        };

        return new Border
        {
            Background = null,
            Child = chart,
            SnapsToDevicePixels = true,
            IsHitTestVisible = true
        };
    }

    public void UpdateHostElement(FrameworkElement host, object rowSource, bool isSelected, bool isCurrentCell)
    {
        if (host is not Border border || border.Child is not ChromatogramControl chart)
        {
            return;
        }

        var seed = _plottoSeedGetter(rowSource);
        border.Tag = seed;
        var points = MainWindow.CreateChromatogramPoints(seed);
        var sharedEditor = global::Plotto.Charting.Hosting.PlottoPresetCharts.Editor;
        EnsureSharedEditorHook(sharedEditor);
        if (isCurrentCell)
        {
            var editorMoved = false;
            if (!ReferenceEquals(border.Child, sharedEditor))
            {
                if (sharedEditor.Parent is Border previousHost)
                {
                    // Always leave edit mode when moving the shared editor to another cell.
                    sharedEditor.RenderMode = ChartRenderMode.Renderer;
                    sharedEditor.IsHitTestVisible = false;
                    var previousRenderer = CreateRendererForSeed(previousHost, GetSeed(previousHost));
                    previousHost.Child = previousRenderer;
                }

                border.Child = sharedEditor;
                editorMoved = true;
            }

            var prevSeed = sharedEditor.Tag is int ps ? (int?)ps : null;
            var seedChanged = prevSeed != seed;

            sharedEditor.Points = points;

            if (editorMoved || seedChanged)
            {
                sharedEditor.ResetIntegrationDisplay();
            }

            sharedEditor.Tag = seed;
            sharedEditor.IsHitTestVisible = true;
            border.IsHitTestVisible = true;
        }
        else
        {
            if (ReferenceEquals(border.Child, sharedEditor))
            {
                sharedEditor.RenderMode = ChartRenderMode.Renderer;
                sharedEditor.IsHitTestVisible = false;
                border.Child = CreateRendererForSeed(border, seed);
            }
            else
            {
                chart.Points = points;
                chart.IsHitTestVisible = true;
            }

            border.IsHitTestVisible = true;
        }

        // Selection and current-cell chrome are drawn by Griddo (SelectionBackground + DrawCurrentCellOverlay).
        // A border here draws a second blue frame inside the cell (nested rect).
        border.BorderThickness = new Thickness(0);
        border.BorderBrush = null;
    }

    private static int GetSeed(Border border)
    {
        return border.Tag is int seed ? seed : 0;
    }

    private static ChromatogramControl CreateRendererForSeed(Border border, int seed)
    {
        border.Tag = seed;
        var chart = new ChromatogramControl
        {
            RequireActivationClick = false,
            RenderMode = ChartRenderMode.Renderer,
            EnableInlineEditing = false,
            EnableMouseInteractions = true,
            ChartTitle = "Chromatogram",
            AxisLabelX = "Time",
            AxisLabelY = "Intensity",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = true,
            Points = MainWindow.CreateChromatogramPoints(seed)
        };
        return chart;
    }

    private static void EnsureSharedEditorHook(ChromatogramControl editor)
    {
        if (_sharedEditorHooked)
        {
            return;
        }

        var descriptor = DependencyPropertyDescriptor.FromProperty(
            SkiaChartBaseControl.RenderModeProperty,
            typeof(ChromatogramControl));
        descriptor?.AddValueChanged(editor, (_, _) => InvalidateOwningGrid(editor));
        _sharedEditorHooked = true;
    }

    private static void InvalidateOwningGrid(DependencyObject node)
    {
        var current = node;
        while (current is not null)
        {
            if (current is global::Griddo.Grid.Griddo griddo)
            {
                griddo.InvalidateVisual();
                return;
            }

            current = VisualTreeHelper.GetParent(current);
        }
    }

    public bool IsHostInEditMode(FrameworkElement host)
    {
        return host is Border { Child: ChromatogramControl chart } &&
               ReferenceEquals(chart, global::Plotto.Charting.Hosting.PlottoPresetCharts.Editor) &&
               chart.RenderMode == ChartRenderMode.Editor;
    }

    public void SetHostEditMode(FrameworkElement host, bool isEditing)
    {
        if (host is not Border { Child: ChromatogramControl chart } border)
        {
            return;
        }

        chart.RenderMode = isEditing ? ChartRenderMode.Editor : ChartRenderMode.Renderer;
        chart.IsHitTestVisible = true;
        border.IsHitTestVisible = true;
    }

    public bool TryHandleHostedMouseWheel(FrameworkElement host, MouseWheelEventArgs e)
    {
        if (host is not Border { Child: SkiaChartBaseControl chart })
        {
            return false;
        }

        return chart.ApplyWheelZoomFromRoute(e);
    }

    public void SyncHostedUiScale(FrameworkElement host, double contentScale)
    {
        if (host is Border { Child: SkiaChartBaseControl chart })
        {
            chart.UiScale = contentScale;
        }
        else if (host is SkiaChartBaseControl chart2)
        {
            chart2.UiScale = contentScale;
        }
    }

    public void ApplyPlotDirectEditOption(FrameworkElement host, bool gridUsesHostedPlotDirectMouseDown)
    {
        static void ApplyOne(ChromatogramControl? chart, bool enabled)
        {
            if (chart is null)
            {
                return;
            }

            chart.DeferRendererActivationToParent = enabled;
        }

        if (host is Border { Child: ChromatogramControl ch })
        {
            ApplyOne(ch, gridUsesHostedPlotDirectMouseDown);
        }

        ApplyOne(global::Plotto.Charting.Hosting.PlottoPresetCharts.Editor, gridUsesHostedPlotDirectMouseDown);
    }

    public void RelayDirectEditMouseDown(FrameworkElement host, MouseButtonEventArgs eFromGrid)
    {
        if (host is not Border { Child: ChromatogramControl chart })
        {
            return;
        }

        if (eFromGrid.ChangedButton == MouseButton.Right && eFromGrid.ClickCount == 2)
        {
            chart.Focus();
            chart.ZoomOutCompletely();
            return;
        }

        host.UpdateLayout();
        chart.UpdateLayout();
        chart.ResetHitTestGeometrySync();

        var routed = new MouseButtonEventArgs(eFromGrid.MouseDevice, eFromGrid.Timestamp, eFromGrid.ChangedButton)
        {
            RoutedEvent = Mouse.MouseDownEvent,
            Source = chart,
        };
        chart.Focus();
        chart.RaiseEvent(routed);
    }

    public void RelayDirectEditMouseUp(FrameworkElement host, MouseButtonEventArgs eFromGrid)
    {
        if (host is not Border { Child: ChromatogramControl chart })
        {
            return;
        }

        var routed = new MouseButtonEventArgs(eFromGrid.MouseDevice, eFromGrid.Timestamp, eFromGrid.ChangedButton)
        {
            RoutedEvent = Mouse.MouseUpEvent,
            Source = chart,
        };
        chart.Focus();
        chart.RaiseEvent(routed);
    }

    public bool TryGetClipboardHtmlFragment(
        FrameworkElement? host,
        object rowSource,
        int cellWidthPx,
        int cellHeightPx,
        out string htmlFragment)
    {
        htmlFragment = string.Empty;
        var seed = _plottoSeedGetter(rowSource);
        var w = Math.Max(120, cellWidthPx);
        var h = Math.Max(80, cellHeightPx);
        const int maxEdge = 480;
        if (w > maxEdge || h > maxEdge)
        {
            var scale = Math.Min((double)maxEdge / w, (double)maxEdge / h);
            w = Math.Max(1, (int)(w * scale));
            h = Math.Max(1, (int)(h * scale));
        }

        SkiaChartBaseControl chart;
        if (host is Border border && border.Child is SkiaChartBaseControl live)
        {
            chart = live;
        }
        else
        {
            chart = CreateRendererForSeed(new Border(), seed);
        }

        var png = chart.GetChartPngBytes(w, h);
        var b64 = Convert.ToBase64String(png);
        htmlFragment =
            "<img src=\"data:image/png;base64," + b64 + "\" alt=\"\" width=\"" + w + "\" height=\"" + h + "\" />";
        return true;
    }
}
