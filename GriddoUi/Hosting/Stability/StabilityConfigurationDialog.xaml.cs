using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Griddo.Editing;
using Griddo.Fields;
using Griddo.Hosting.Configuration;
using Griddo.Hosting.Html;
using Griddo.Hosting.Plot;

namespace GriddoUi.Hosting.Stability;

public partial class StabilityConfigurationDialog : Window
{
    private readonly List<StabilitySeriesEditRecord> _rows = [];
    private readonly Action<StabilityFieldConfiguration>? _previewApply;

    public StabilityConfigurationDialog(
        IStabilityFieldLayoutTarget seed,
        IReadOnlyList<IGriddoFieldView> allFields,
        Action<StabilityFieldConfiguration>? previewApply = null)
    {
        InitializeComponent();
        _previewApply = previewApply;
        BuildSeriesGridFields();
        BuildGeneralGridFields();
        SeedFrom(seed, allFields);
        UpdateMoveButtonsVisibility();
    }

    public StabilityFieldConfiguration? Result { get; private set; }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        UpdateMoveButtonsVisibility();
    }

    private void UpdateMoveButtonsVisibility()
    {
        var visible = MainTabs.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        MoveUpButton.Visibility = visible;
        MoveDownButton.Visibility = visible;
    }

    private void SeedFrom(IStabilityFieldLayoutTarget seed, IReadOnlyList<IGriddoFieldView> allFields)
    {
        GeneralGrid.Records.Clear();
        GeneralGrid.Records.Add(new StabilityGeneralSettingRecord { Label = "Plot label", Value = seed.Label ?? string.Empty });

        var savedByIndex = seed.Series.ToDictionary(s => s.SourceFieldIndex);
        var excluded = new HashSet<int>();
        for (var sourceFieldIndex = 0; sourceFieldIndex < allFields.Count; sourceFieldIndex++)
        {
            var field = allFields[sourceFieldIndex];
            if (field is IHtmlFieldLayoutTarget || field is IPlotFieldLayoutTarget || field is IStabilityFieldLayoutTarget)
            {
                excluded.Add(sourceFieldIndex);
            }
        }

        var configuredOrder = seed.Series
            .Select(s => s.SourceFieldIndex)
            .Where(i => i >= 0 && i < allFields.Count && !excluded.Contains(i))
            .Distinct()
            .ToList();
        var remainingOrder = Enumerable.Range(0, allFields.Count)
            .Where(i => !excluded.Contains(i) && !configuredOrder.Contains(i))
            .ToList();
        var ordered = configuredOrder.Concat(remainingOrder);

        SeriesGrid.Records.Clear();
        _rows.Clear();
        foreach (var sourceFieldIndex in ordered)
        {
            var field = allFields[sourceFieldIndex];
            if (!IsNumericCandidate(field))
            {
                continue;
            }

            var hit = savedByIndex.TryGetValue(sourceFieldIndex, out var config) ? config : null;
            var row = new StabilitySeriesEditRecord
            {
                SourceFieldIndex = sourceFieldIndex,
                Header = field.Header ?? string.Empty,
                Enabled = hit?.Enabled ?? false,
                ShowSdLines = hit?.ShowSdLines ?? true,
                ShowLine = hit?.ShowLine ?? false,
                ShowMarker = hit?.ShowMarker ?? true,
                Color = hit?.Color ?? string.Empty,
                AxisSide = hit?.AxisSide ?? StabilityAxisSide.Left
            };
            _rows.Add(row);
            SeriesGrid.Records.Add(row);
        }
    }

    private StabilityFieldConfiguration BuildResult()
    {
        var label = GeneralGrid.Records.OfType<StabilityGeneralSettingRecord>().FirstOrDefault()?.Value ?? string.Empty;
        return new StabilityFieldConfiguration
        {
            Label = label,
            Series = SeriesGrid.Records
                .OfType<StabilitySeriesEditRecord>()
                .Select(r => new StabilitySeriesConfiguration
                {
                    SourceFieldIndex = r.SourceFieldIndex,
                    Enabled = r.Enabled,
                    ShowSdLines = r.ShowSdLines,
                    ShowLine = r.ShowLine,
                    ShowMarker = r.ShowMarker,
                    Color = r.Color ?? string.Empty,
                    AxisSide = r.AxisSide
                })
                .ToList()
        };
    }

    private static bool IsNumericCandidate(IGriddoFieldView field)
    {
        if (field.Editor == GriddoCellEditors.Number)
        {
            return true;
        }

        var header = field.Header ?? string.Empty;
        return header.Contains("intens", StringComparison.OrdinalIgnoreCase)
               || header.Contains("conc", StringComparison.OrdinalIgnoreCase)
               || header.Contains("retention", StringComparison.OrdinalIgnoreCase)
               || header.Contains("rt", StringComparison.OrdinalIgnoreCase);
    }

    private void BuildSeriesGridFields()
    {
        SeriesGrid.Fields.Clear();
        SeriesGrid.Fields.Add(new GriddoBoolFieldView("Use", 50, r => ((StabilitySeriesEditRecord)r).Enabled, (r, v) =>
        {
            if (!AsBool(v, out var enabled))
            {
                return false;
            }

            ((StabilitySeriesEditRecord)r).Enabled = enabled;
            return true;
        }));
        SeriesGrid.Fields.Add(new GriddoFieldView("Field", 220, r => ((StabilitySeriesEditRecord)r).Header, static (_, _) => false, GriddoCellEditors.Text));
        SeriesGrid.Fields.Add(new GriddoFieldView("Color", 110, r => ((StabilitySeriesEditRecord)r).Color, (r, v) => { ((StabilitySeriesEditRecord)r).Color = v?.ToString() ?? string.Empty; return true; }, GriddoCellEditors.KnownColorsDropdown));
        SeriesGrid.Fields.Add(new GriddoBoolFieldView("Line", 60, r => ((StabilitySeriesEditRecord)r).ShowLine, (r, v) => SetBool(v, b => ((StabilitySeriesEditRecord)r).ShowLine = b)));
        SeriesGrid.Fields.Add(new GriddoBoolFieldView("Marker", 70, r => ((StabilitySeriesEditRecord)r).ShowMarker, (r, v) => SetBool(v, b => ((StabilitySeriesEditRecord)r).ShowMarker = b)));
        SeriesGrid.Fields.Add(new GriddoBoolFieldView("SD", 55, r => ((StabilitySeriesEditRecord)r).ShowSdLines, (r, v) => SetBool(v, b => ((StabilitySeriesEditRecord)r).ShowSdLines = b)));
        SeriesGrid.Fields.Add(new GriddoBoolFieldView("Y axis", 72, r => HasYAxis((StabilitySeriesEditRecord)r), (r, v) =>
        {
            if (!AsBool(v, out var isOn))
            {
                return false;
            }

            var row = (StabilitySeriesEditRecord)r;
            if (isOn)
            {
                foreach (var other in SeriesGrid.Records.OfType<StabilitySeriesEditRecord>())
                {
                    if (ReferenceEquals(other, row))
                    {
                        continue;
                    }

                    SetYAxis(other, false);
                }
                SetYAxis(row, true);
            }
            else
            {
                SetYAxis(row, false);
            }
            return true;
        }));
        SeriesGrid.Fields.Add(new GriddoBoolFieldView("X axis", 72, r => HasXAxis((StabilitySeriesEditRecord)r), (r, v) =>
        {
            if (!AsBool(v, out var isOn))
            {
                return false;
            }

            var row = (StabilitySeriesEditRecord)r;
            if (isOn)
            {
                foreach (var other in SeriesGrid.Records.OfType<StabilitySeriesEditRecord>())
                {
                    if (ReferenceEquals(other, row))
                    {
                        continue;
                    }

                    SetXAxis(other, false);
                }
                SetXAxis(row, true);
            }
            else
            {
                SetXAxis(row, false);
            }
            return true;
        }));
    }

    private static bool HasYAxis(StabilitySeriesEditRecord row) =>
        row.AxisSide is StabilityAxisSide.Left or StabilityAxisSide.Both;

    private static bool HasXAxis(StabilitySeriesEditRecord row) =>
        row.AxisSide is StabilityAxisSide.Right or StabilityAxisSide.Both;

    private static void SetYAxis(StabilitySeriesEditRecord row, bool isOn)
    {
        row.AxisSide = row.AxisSide switch
        {
            StabilityAxisSide.None => isOn ? StabilityAxisSide.Left : StabilityAxisSide.None,
            StabilityAxisSide.Left => isOn ? StabilityAxisSide.Left : StabilityAxisSide.None,
            StabilityAxisSide.Right => isOn ? StabilityAxisSide.Both : StabilityAxisSide.Right,
            StabilityAxisSide.Both => isOn ? StabilityAxisSide.Both : StabilityAxisSide.Right,
            _ => row.AxisSide
        };
    }

    private static void SetXAxis(StabilitySeriesEditRecord row, bool isOn)
    {
        row.AxisSide = row.AxisSide switch
        {
            StabilityAxisSide.None => isOn ? StabilityAxisSide.Right : StabilityAxisSide.None,
            StabilityAxisSide.Left => isOn ? StabilityAxisSide.Both : StabilityAxisSide.Left,
            StabilityAxisSide.Right => isOn ? StabilityAxisSide.Right : StabilityAxisSide.None,
            StabilityAxisSide.Both => isOn ? StabilityAxisSide.Both : StabilityAxisSide.Left,
            _ => row.AxisSide
        };
    }

    private void BuildGeneralGridFields()
    {
        GeneralGrid.Fields.Clear();
        GeneralGrid.Fields.Add(new GriddoFieldView("Setting", 220, _ => "Plot label", static (_, _) => false, GriddoCellEditors.Text));
        GeneralGrid.Fields.Add(new GriddoFieldView("Value", 300, r => ((StabilityGeneralSettingRecord)r).Value, (r, v) => { ((StabilityGeneralSettingRecord)r).Value = v?.ToString() ?? string.Empty; return true; }, GriddoCellEditors.Text));
    }

    private static bool SetBool(object? v, Action<bool> apply)
    {
        if (!AsBool(v, out var b))
        {
            return false;
        }

        apply(b);
        return true;
    }

    private static bool AsBool(object? v, out bool b)
    {
        if (v is bool hit)
        {
            b = hit;
            return true;
        }

        b = false;
        return false;
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _ = SeriesGrid.TryMoveSelectedRecordsStep(-1);
    }

    private void MoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _ = SeriesGrid.TryMoveSelectedRecordsStep(1);
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Result = BuildResult();
        _previewApply?.Invoke(Result);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Result = BuildResult();
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        DialogResult = false;
    }

    private sealed class StabilityGeneralSettingRecord
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    private sealed class StabilitySeriesEditRecord
    {
        public int SourceFieldIndex { get; set; }
        public string Header { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public bool ShowSdLines { get; set; } = true;
        public bool ShowLine { get; set; }
        public bool ShowMarker { get; set; } = true;
        public string Color { get; set; } = string.Empty;
        public StabilityAxisSide AxisSide { get; set; } = StabilityAxisSide.Left;
    }
}
