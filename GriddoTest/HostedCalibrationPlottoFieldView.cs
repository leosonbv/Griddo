using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using System.Net;
using Plotto.Charting.Controls;
using Plotto.Charting.Core;
using Griddo;
using Griddo.Fields;
using Griddo.Editing;
using GriddoModelView;

namespace GriddoTest;

public sealed class HostedCalibrationPlottoFieldView : IGriddoHostedFieldView, IGriddoFieldSourceMember, IGriddoFieldSourceObject, IGriddoFieldDescriptionView, IPlotFieldLayoutTarget
{
    private static bool _sharedEditorHooked;
    private readonly Func<object, int> _seedGetter;
    private readonly Func<IReadOnlyList<IGriddoFieldView>>? _allFieldsAccessor;

    public HostedCalibrationPlottoFieldView(
        string header,
        double width,
        Func<object, int> seedGetter,
        Func<IReadOnlyList<IGriddoFieldView>>? allFieldsAccessor = null,
        string sourceObjectName = "",
        string sourceMemberName = "")
    {
        Header = header;
        Width = width;
        _seedGetter = seedGetter;
        _allFieldsAccessor = allFieldsAccessor;
        SourceObjectName = sourceObjectName;
        SourceMemberName = sourceMemberName;
        Editor = GriddoCellEditors.Text;
        ContentAlignment = TextAlignment.Left;
    }

    public string Header { get; set; }
    public string Description { get; set; } = string.Empty;
    public string PlotTypeKey => "Calibration curve";
    public string TitleSelection { get; set; } = "Calibration curve";
    public bool ShowTitle { get; set; } = true;
    public List<PlotTitleSegmentConfiguration> TitleSegments { get; set; } = [];
    public bool ShowXAxis { get; set; } = true;
    public bool ShowYAxis { get; set; } = true;
    public bool ShowXAxisTitle { get; set; } = true;
    public bool ShowYAxisTitle { get; set; } = true;
    public string XAxis { get; set; } = string.Empty;
    public string YAxis { get; set; } = string.Empty;
    public string XAxisTitle { get; set; } = "Concentration";
    public string YAxisTitle { get; set; } = "Response";
    public string Label { get; set; } = string.Empty;
    public int XAxisLabelPrecision { get; set; } = 2;
    public int YAxisLabelPrecision { get; set; } = 2;
    public string XAxisLabelFormat { get; set; } = string.Empty;
    public string YAxisLabelFormat { get; set; } = string.Empty;
    public double AxisFontSize { get; set; } = 10d;
    public double TitleFontSize { get; set; } = 11d;
    public bool ChromatogramShowPeaks { get; set; }
    public bool CalibrationShowRegression { get; set; }
    public bool SpectrumNormalizeIntensity { get; set; }

    public string SourceObjectName { get; }
    public string SourceMemberName { get; }
    public double Width { get; }
    public bool Fill { get; set; }
    public bool IsHtml => false;
    public TextAlignment ContentAlignment { get; }
    public IGriddoCellEditor Editor { get; }

    public object? GetValue(object recordSource) => null;

    public bool TrySetValue(object recordSource, object? value) => false;

    public string FormatValue(object? value) => string.Empty;

    public FrameworkElement CreateHostElement()
    {
        var chart = new CalibrationCurveControl
        {
            RequireActivationClick = false,
            RenderMode = ChartRenderMode.Renderer,
            EnableInlineEditing = false,
            EnableMouseInteractions = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = true,
            FitMode = CalibrationFitMode.Linear,
            CalibrationPoints = MainWindow.CreateCalibrationPoints(0)
        };
        ApplyChartSettings(chart, null);

        return new Border
        {
            Background = null,
            Child = chart,
            SnapsToDevicePixels = true,
            ClipToBounds = true,
            IsHitTestVisible = true
        };
    }

    public void UpdateHostElement(FrameworkElement host, object recordSource, bool isSelected, bool isCurrentCell)
    {
        if (host is not Border border || border.Child is not CalibrationCurveControl chart)
        {
            return;
        }

        var seed = _seedGetter(recordSource);
        border.Tag = seed;
        var calibrationPoints = MainWindow.CreateCalibrationPoints(seed);
        var fitMode = (CalibrationFitMode)(Math.Abs(seed) % 4);

        var sharedEditor = global::Plotto.Charting.Hosting.PlottoPresetCharts.CalibrationEditor;
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
            ApplyChartSettings(sharedEditor, recordSource);

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
                ApplyChartSettings(chart, recordSource);
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

    private CalibrationCurveControl CreateRendererForSeed(Border border, int seed)
    {
        border.Tag = seed;
        var chart = new CalibrationCurveControl
        {
            RequireActivationClick = false,
            RenderMode = ChartRenderMode.Renderer,
            EnableInlineEditing = false,
            EnableMouseInteractions = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = true,
            FitMode = (CalibrationFitMode)(Math.Abs(seed) % 4),
            CalibrationPoints = MainWindow.CreateCalibrationPoints(seed)
        };
        ApplyChartSettings(chart, null);
        return chart;
    }

    private void ApplyChartSettings(SkiaChartBaseControl chart, object? recordSource)
    {
        chart.ChartTitle = BuildTitleHtml(recordSource);
        chart.ShowChartTitle = ShowTitle;
        chart.AxisLabelX = ShowXAxisTitle ? XAxisTitle : string.Empty;
        chart.AxisLabelY = ShowYAxisTitle ? YAxisTitle : string.Empty;
        chart.ChartLabel = Label;
        chart.AxisLabelPrecisionX = Math.Clamp(XAxisLabelPrecision, 0, 10);
        chart.AxisLabelPrecisionY = Math.Clamp(YAxisLabelPrecision, 0, 10);
        chart.AxisLabelFormatX = XAxisLabelFormat ?? string.Empty;
        chart.AxisLabelFormatY = YAxisLabelFormat ?? string.Empty;
        chart.AxisFontSize = AxisFontSize;
        chart.TitleFontSize = TitleFontSize;
        chart.ShowXAxis = ShowXAxis;
        chart.ShowYAxis = ShowYAxis;
    }

    private string BuildTitleHtml(object? recordSource)
    {
        if (recordSource is null || _allFieldsAccessor is null)
        {
            return string.Empty;
        }

        var allFields = _allFieldsAccessor();
        var segments = TitleSegments.Where(s => s.Enabled).ToList();
        if (segments.Count == 0)
        {
            return string.Empty;
        }

        var rows = new List<string>(segments.Count);
        foreach (var segment in segments)
        {
            if (segment.SourceFieldIndex < 0 || segment.SourceFieldIndex >= allFields.Count)
            {
                continue;
            }

            var field = allFields[segment.SourceFieldIndex];
            var label = string.IsNullOrWhiteSpace(segment.AbbreviatedHeaderOverride)
                ? (field.Header ?? string.Empty)
                : segment.AbbreviatedHeaderOverride;
            var value = field.GetValue(recordSource);
            var rendered = field.IsHtml
                ? (value?.ToString() ?? string.Empty)
                : BuildStyledText(WebUtility.HtmlEncode(field.FormatValue(value)), field);
            if (!segment.WordWrap)
            {
                rendered = rendered.Replace(" ", "\u00A0", StringComparison.Ordinal);
            }

            var breakBefore = segment.AddLineBreakAfter ? " data-break-before='1'" : string.Empty;
            rows.Add($"<tr{breakBefore}><td><b>{WebUtility.HtmlEncode(label)}</b></td><td>{rendered}</td></tr>");
        }

        if (rows.Count == 0)
        {
            return string.Empty;
        }

        return "<table style='border-collapse:collapse;border:none'><tbody>" +
               string.Join(string.Empty, rows) +
               "</tbody></table>";
    }

    private static string BuildStyledText(string text, IGriddoFieldView field)
    {
        var styles = new List<string>();
        if (field is IGriddoFieldColorView colorView)
        {
            if (!string.IsNullOrWhiteSpace(colorView.ForegroundColor))
            {
                styles.Add($"color:{WebUtility.HtmlEncode(colorView.ForegroundColor)}");
            }

            if (!string.IsNullOrWhiteSpace(colorView.BackgroundColor))
            {
                styles.Add($"background-color:{WebUtility.HtmlEncode(colorView.BackgroundColor)}");
            }
        }

        if (field is IGriddoFieldFontView fontView)
        {
            if (!string.IsNullOrWhiteSpace(fontView.FontFamilyName))
            {
                styles.Add($"font-family:{WebUtility.HtmlEncode(fontView.FontFamilyName)}");
            }

            if (fontView.FontSize > 0)
            {
                styles.Add($"font-size:{fontView.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}px");
            }

            if (!string.IsNullOrWhiteSpace(fontView.FontStyleName))
            {
                styles.Add($"font-style:{WebUtility.HtmlEncode(fontView.FontStyleName)}");
            }
        }

        return styles.Count == 0
            ? text
            : $"<span style='{string.Join(";", styles)}'>{text}</span>";
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
               ReferenceEquals(chart, global::Plotto.Charting.Hosting.PlottoPresetCharts.CalibrationEditor) &&
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

        ApplyOne(global::Plotto.Charting.Hosting.PlottoPresetCharts.CalibrationEditor, gridUsesHostedPlotDirectMouseDown);
    }

    public void RelayDirectEditMouseDown(FrameworkElement host, MouseButtonEventArgs eFromGrid)
    {
        if (host is not Border { Child: CalibrationCurveControl chart })
        {
            return;
        }

        if (eFromGrid is { ChangedButton: MouseButton.Right, ClickCount: 2 })
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
        object recordSource,
        int cellWidthPx,
        int cellHeightPx,
        out string htmlFragment)
    {
        htmlFragment = string.Empty;
        var seed = _seedGetter(recordSource);
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
        if (host is Border { Child: SkiaChartBaseControl live })
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
