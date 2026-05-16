using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Griddo.Editing;
using Griddo.Fields;
using Griddo.Hosting.Configuration;
using Plotto.Charting.Controls;
using Plotto.Charting.Core;

namespace Griddo.Hosting.Plot;

public sealed class HostedStabilityFieldView : IGriddoHostedFieldView, IGriddoFieldSourceMember, IGriddoFieldSourceObject, IGriddoFieldDescriptionView, IStabilityFieldLayoutTarget
{
    private const int MaxPointsPerSeries = 100;
    private readonly Func<IReadOnlyList<IGriddoFieldView>> _allFieldsAccessor;
    private readonly Func<IReadOnlyList<object>> _recordsAccessor;

    public HostedStabilityFieldView(
        string header,
        double width,
        Func<IReadOnlyList<IGriddoFieldView>> allFieldsAccessor,
        Func<IReadOnlyList<object>> recordsAccessor,
        string sourceObjectName = "",
        string sourceMemberName = "")
    {
        Header = header;
        Width = width;
        _allFieldsAccessor = allFieldsAccessor ?? throw new ArgumentNullException(nameof(allFieldsAccessor));
        _recordsAccessor = recordsAccessor ?? throw new ArgumentNullException(nameof(recordsAccessor));
        SourceObjectName = sourceObjectName;
        SourceMemberName = sourceMemberName;
        Editor = GriddoCellEditors.Text;
        ContentAlignment = TextAlignment.Left;
    }

    public string Header { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SourceMemberName { get; }
    public string SourceObjectName { get; }
    public string Label { get; set; } = "Stability";
    public List<StabilitySeriesConfiguration> Series { get; set; } = [];
    public double Width { get; }
    public int FieldFill { get; set; }
    public bool IsHtml => false;
    public TextAlignment ContentAlignment { get; }
    public IGriddoCellEditor Editor { get; }

    public object? GetValue(object recordSource) => null;
    public bool TrySetValue(object recordSource, object? value) => false;
    public string FormatValue(object? value) => string.Empty;

    public FrameworkElement CreateHostElement()
    {
        var chart = new StabilityScatterControl
        {
            RenderMode = ChartRenderMode.Renderer,
            IsHitTestVisible = false,
            EnableInlineEditing = true,
            EnableMouseInteractions = true,
            DeferRendererActivationToParent = false
        };

        chart.ContextMenu = BuildContextMenu(chart);
        return new Border
        {
            BorderThickness = new Thickness(0),
            Child = chart
        };
    }

    public void UpdateHostElement(FrameworkElement host, object recordSource, bool isSelected, bool isCurrentCell)
    {
        _ = isSelected;
        _ = isCurrentCell;
        if (host is not Border { Child: StabilityScatterControl chart })
        {
            return;
        }

        _ = recordSource;
        var configured = BuildSeriesFromConfiguredFields();
        chart.SetSeries(configured);
        chart.ShowYAxis = chart.HasLeftAxis;
        chart.ChartTitle = Label ?? string.Empty;
        chart.ShowChartTitle = !string.IsNullOrWhiteSpace(chart.ChartTitle);
    }

    public bool IsHostInEditMode(FrameworkElement host) =>
        host is Border { Child: StabilityScatterControl chart } && chart.RenderMode == ChartRenderMode.Editor;

    public void SetHostEditMode(FrameworkElement host, bool isEditing)
    {
        if (host is not Border { Child: StabilityScatterControl chart })
        {
            return;
        }

        chart.RenderMode = isEditing ? ChartRenderMode.Editor : ChartRenderMode.Renderer;
        chart.IsHitTestVisible = isEditing;
        chart.DeferRendererActivationToParent = false;
        if (isEditing)
        {
            chart.Focus();
        }
    }

    public bool TryHandleHostedMouseWheel(FrameworkElement host, MouseWheelEventArgs e) =>
        host is Border { Child: SkiaChartBaseControl chart } && chart.ApplyWheelZoomFromRoute(e);

    public void SyncHostedUiScale(FrameworkElement host, double contentScale)
    {
        if (host is Border { Child: SkiaChartBaseControl chart })
        {
            chart.UiScale = contentScale;
        }
    }

    public bool TryGetClipboardHtmlFragment(FrameworkElement? host, object recordSource, int cellWidthPx, int cellHeightPx, out string htmlFragment)
    {
        _ = recordSource;
        htmlFragment = string.Empty;
        if (host is not Border { Child: SkiaChartBaseControl chart })
        {
            return false;
        }

        var width = Math.Max(120, cellWidthPx);
        var height = Math.Max(80, cellHeightPx);
        var png = chart.GetChartPngBytes(width, height);
        htmlFragment = $"<img src=\"data:image/png;base64,{Convert.ToBase64String(png)}\" alt=\"\" width=\"{width}\" height=\"{height}\" />";
        return true;
    }

    public bool ShouldRelayLeftDoubleClickWhileInHostedEditMode() => true;

    public void RelayDirectEditMouseDown(FrameworkElement host, MouseButtonEventArgs eFromGrid)
    {
        if (host is not Border { Child: StabilityScatterControl chart })
        {
            return;
        }

        if (eFromGrid is { ChangedButton: MouseButton.Right, ClickCount: 2 })
        {
            chart.Focus();
            chart.HandleExternalRightDoubleClick(eFromGrid.GetPosition(chart));
            return;
        }

        if (!chart.IsKeyboardFocusWithin)
        {
            chart.Focus();
        }

        host.UpdateLayout();
        chart.UpdateLayout();
        chart.ResetHitTestGeometrySync();

        var routed = new MouseButtonEventArgs(eFromGrid.MouseDevice, eFromGrid.Timestamp, eFromGrid.ChangedButton)
        {
            RoutedEvent = Mouse.MouseDownEvent,
            Source = chart
        };
        chart.RaiseEvent(routed);
    }

    public void RelayDirectEditMouseUp(FrameworkElement host, MouseButtonEventArgs eFromGrid)
    {
        if (host is not Border { Child: StabilityScatterControl chart })
        {
            return;
        }

        var routed = new MouseButtonEventArgs(eFromGrid.MouseDevice, eFromGrid.Timestamp, eFromGrid.ChangedButton)
        {
            RoutedEvent = Mouse.MouseUpEvent,
            Source = chart
        };
        chart.RaiseEvent(routed);
    }

    private static ContextMenu BuildContextMenu(StabilityScatterControl chart)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("Zoom active Y (points)", (_, _) => chart.ZoomOutActiveYToPoints(includeZero: false)));
        menu.Items.Add(CreateMenuItem("Zoom active Y (include 0)", (_, _) => chart.ZoomOutActiveYToPoints(includeZero: true)));
        menu.Items.Add(CreateMenuItem("Zoom active Y: 0 to +3SD (+5%)", (_, _) => chart.ZoomActiveYToZeroToPlus3Sd()));
        menu.Items.Add(CreateMenuItem("Zoom active Y: -3SD to +3SD (+5%)", (_, _) => chart.ZoomActiveYToPlusMinus3Sd()));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Zoom out X (points)", (_, _) => chart.ZoomOutXToPoints()));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Zoom out all (points)", (_, _) => chart.ZoomOutAllToPoints(includeZero: false)));
        menu.Items.Add(CreateMenuItem("Zoom out all (X + Y + 0)", (_, _) => chart.ZoomOutAllToPoints(includeZero: true)));
        return menu;
    }

    private static MenuItem CreateMenuItem(string header, RoutedEventHandler onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += onClick;
        return item;
    }

    private IReadOnlyList<StabilityScatterControl.SeriesData> BuildSeriesFromConfiguredFields()
    {
        var allFields = _allFieldsAccessor();
        var allRecords = _recordsAccessor();
        if (allFields.Count == 0 || allRecords.Count == 0)
        {
            return [];
        }

        var configuredSeries = Series.Where(static s => s.Enabled).ToList();
        if (configuredSeries.Count == 0)
        {
            return [];
        }

        var result = new List<StabilityScatterControl.SeriesData>(configuredSeries.Count);
        foreach (var cfg in configuredSeries)
        {
            if (cfg.SourceFieldIndex < 0 || cfg.SourceFieldIndex >= allFields.Count)
            {
                continue;
            }

            var field = allFields[cfg.SourceFieldIndex];
            var points = new List<ChartPoint>(allRecords.Count);
            var step = Math.Max(1, allRecords.Count / MaxPointsPerSeries);
            for (var i = 0; i < allRecords.Count; i += step)
            {
                var raw = field.GetValue(allRecords[i]);
                if (!TryGetNumeric(raw, out var y))
                {
                    continue;
                }

                points.Add(new ChartPoint(i + 1, y));
            }

            // Ensure the final record contributes even when stepping skips the tail.
            if (allRecords.Count > 0)
            {
                var lastIndex = allRecords.Count - 1;
                if (points.Count == 0 || points[^1].X < lastIndex + 1)
                {
                    var lastRaw = field.GetValue(allRecords[lastIndex]);
                    if (TryGetNumeric(lastRaw, out var lastY))
                    {
                        points.Add(new ChartPoint(lastIndex + 1, lastY));
                    }
                }
            }

            if (points.Count == 0)
            {
                continue;
            }

            var mean = points.Average(static p => p.Y);
            var variance = points.Average(p =>
            {
                var delta = p.Y - mean;
                return delta * delta;
            });

            result.Add(new StabilityScatterControl.SeriesData
            {
                Label = field.Header ?? string.Empty,
                Color = cfg.Color,
                ShowSdLines = cfg.ShowSdLines,
                ShowLine = cfg.ShowLine,
                ShowMarker = cfg.ShowMarker,
                AxisSide = cfg.AxisSide,
                Points = points,
                Mean = mean,
                StandardDeviation = Math.Sqrt(Math.Max(0d, variance))
            });
        }

        return result;
    }

    private static bool TryGetNumeric(object? raw, out double value)
    {
        switch (raw)
        {
            case null:
                value = 0;
                return false;
            case byte b:
                value = b;
                return true;
            case sbyte sb:
                value = sb;
                return true;
            case short s:
                value = s;
                return true;
            case ushort us:
                value = us;
                return true;
            case int i:
                value = i;
                return true;
            case uint ui:
                value = ui;
                return true;
            case long l:
                value = l;
                return true;
            case ulong ul:
                value = ul;
                return true;
            case float f:
                value = f;
                return true;
            case double d:
                value = d;
                return true;
            case decimal dec:
                value = (double)dec;
                return true;
            default:
                return double.TryParse(raw.ToString(), out value);
        }
    }
}
