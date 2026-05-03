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

public sealed class HostedCalibrationPlottoColumnView : IGriddoHostedColumnView
{
    private static bool _sharedEditorHooked;
    private readonly Func<object, int> _seedGetter;

    public HostedCalibrationPlottoColumnView(string header, double width, Func<object, int> seedGetter)
    {
        Header = header;
        Width = width;
        _seedGetter = seedGetter;
        Editor = GriddoCellEditors.Text;
        ContentAlignment = TextAlignment.Left;
    }

    public string Header { get; }
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
        var chart = new CalibrationCurveControl
        {
            RequireActivationClick = false,
            RenderMode = ChartRenderMode.Renderer,
            EnableInlineEditing = false,
            EnableMouseInteractions = true,
            ChartTitle = "Calibration curve",
            AxisLabelX = "Concentration",
            AxisLabelY = "Response",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = true,
            FitMode = CalibrationFitMode.Linear,
            CalibrationPoints = MainWindow.CreateCalibrationPoints(0)
        };

        return new Border
        {
            Background = null,
            Child = chart,
            SnapsToDevicePixels = true,
            ClipToBounds = true,
            IsHitTestVisible = true
        };
    }

    public void UpdateHostElement(FrameworkElement host, object rowSource, bool isSelected, bool isCurrentCell)
    {
        if (host is not Border border || border.Child is not CalibrationCurveControl chart)
        {
            return;
        }

        var seed = _seedGetter(rowSource);
        border.Tag = seed;
        var calibrationPoints = MainWindow.CreateCalibrationPoints(seed);
        var fitMode = (CalibrationFitMode)(Math.Abs(seed) % 4);

        var sharedEditor = global::Plotto.Hosting.PlottoPresetCharts.CalibrationEditor;
        EnsureSharedEditorHook(sharedEditor);

        if (isCurrentCell)
        {
            if (!ReferenceEquals(border.Child, sharedEditor))
            {
                if (sharedEditor.Parent is Border previousHost)
                {
                    sharedEditor.RenderMode = ChartRenderMode.Renderer;
                    sharedEditor.IsHitTestVisible = false;
                    var previousRenderer = CreateRendererForSeed(previousHost, GetSeed(previousHost));
                    previousHost.Child = previousRenderer;
                }

                border.Child = sharedEditor;
            }

            sharedEditor.CalibrationPoints = calibrationPoints;
            sharedEditor.FitMode = fitMode;

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
                chart.CalibrationPoints = calibrationPoints;
                chart.FitMode = fitMode;
                chart.IsHitTestVisible = true;
            }

            border.IsHitTestVisible = true;
        }

        border.BorderThickness = new Thickness(0);
        border.BorderBrush = null;
        border.Background = null;
    }

    private static int GetSeed(Border border)
    {
        return border.Tag is int seed ? seed : 0;
    }

    private static CalibrationCurveControl CreateRendererForSeed(Border border, int seed)
    {
        border.Tag = seed;
        return new CalibrationCurveControl
        {
            RequireActivationClick = false,
            RenderMode = ChartRenderMode.Renderer,
            EnableInlineEditing = false,
            EnableMouseInteractions = true,
            ChartTitle = "Calibration curve",
            AxisLabelX = "Concentration",
            AxisLabelY = "Response",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = true,
            FitMode = (CalibrationFitMode)(Math.Abs(seed) % 4),
            CalibrationPoints = MainWindow.CreateCalibrationPoints(seed)
        };
    }

    private static void EnsureSharedEditorHook(CalibrationCurveControl editor)
    {
        if (_sharedEditorHooked)
        {
            return;
        }

        var descriptor = DependencyPropertyDescriptor.FromProperty(
            SkiaChartBaseControl.RenderModeProperty,
            typeof(CalibrationCurveControl));
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
        return host is Border { Child: CalibrationCurveControl chart } &&
               ReferenceEquals(chart, global::Plotto.Hosting.PlottoPresetCharts.CalibrationEditor) &&
               chart.RenderMode == ChartRenderMode.Editor;
    }

    public void SetHostEditMode(FrameworkElement host, bool isEditing)
    {
        if (host is not Border { Child: CalibrationCurveControl chart } border)
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
        static void ApplyOne(SkiaChartBaseControl? chart, bool enabled)
        {
            if (chart is null)
            {
                return;
            }

            chart.DeferRendererActivationToParent = enabled;
        }

        if (host is Border { Child: CalibrationCurveControl ch })
        {
            ApplyOne(ch, gridUsesHostedPlotDirectMouseDown);
        }

        ApplyOne(global::Plotto.Hosting.PlottoPresetCharts.CalibrationEditor, gridUsesHostedPlotDirectMouseDown);
    }

    public void RelayDirectEditMouseDown(FrameworkElement host, MouseButtonEventArgs eFromGrid)
    {
        if (host is not Border { Child: CalibrationCurveControl chart })
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
        if (host is not Border { Child: CalibrationCurveControl chart })
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
        var seed = _seedGetter(rowSource);
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
