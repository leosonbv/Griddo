using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Griddo.Fields;
using Griddo.Editing;
using GriddoModelView;
using GriddoTest.HtmlHosting;

namespace GriddoTest.PlotHosting;

public partial class PlotConfigurationDialog : Window
{
    private readonly IPlotFieldLayoutTarget _initial;
    private readonly IReadOnlyList<IGriddoFieldView> _allFields;
    private readonly List<PlotTitleFieldEditRecord> _rows = [];
    private readonly Action<PlotFieldDialogResult>? _previewApply;

    public PlotFieldDialogResult? Result { get; private set; }

    public PlotConfigurationDialog(
        IPlotFieldLayoutTarget initial,
        IReadOnlyList<IGriddoFieldView> allFields,
        Action<PlotFieldDialogResult>? previewApply = null)
    {
        InitializeComponent();
        _initial = initial;
        _allFields = allFields;
        _previewApply = previewApply;
        BuildTitleFieldGridFields();
        BuildGeneralGridFields();
        BuildSpecificGridFields();
        Loaded += (_, _) =>
        {
            LoadFromChart();
            UpdateMoveButtonsVisibility();
        };
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        UpdateMoveButtonsVisibility();
    }

    private void UpdateMoveButtonsVisibility()
    {
        var show = MainTabs.SelectedIndex == 0;
        var visibility = show ? Visibility.Visible : Visibility.Collapsed;
        MoveUpButton.Visibility = visibility;
        MoveDownButton.Visibility = visibility;
    }

    private void LoadFromChart()
    {
        SeedTitleRows();
        GeneralGrid.Records.Clear();
        GeneralGrid.Records.Add(new PlotGeneralSettingRecord(PlotGeneralSettingKind.Label, _initial.Label));
        SeedSpecificRows();
    }

    private void SeedTitleRows()
    {
        _rows.Clear();
        TitleFieldsGrid.Records.Clear();
        var savedByIndex = _initial.TitleSegments.ToDictionary(s => s.SourceFieldIndex);
        var excluded = new HashSet<int>();
        for (var sourceFieldIndex = 0; sourceFieldIndex < _allFields.Count; sourceFieldIndex++)
        {
            var field = _allFields[sourceFieldIndex];
            if (ReferenceEquals(field, _initial) || field is IHtmlFieldLayoutTarget || field is IPlotFieldLayoutTarget)
            {
                excluded.Add(sourceFieldIndex);
            }
        }

        var configuredOrder = _initial.TitleSegments
            .Select(s => s.SourceFieldIndex)
            .Where(i => i >= 0 && i < _allFields.Count && !excluded.Contains(i))
            .Distinct()
            .ToList();
        var remainingOrder = Enumerable.Range(0, _allFields.Count)
            .Where(i => !excluded.Contains(i) && !configuredOrder.Contains(i))
            .ToList();
        var orderedSourceIndices = configuredOrder.Concat(remainingOrder);

        foreach (var sourceFieldIndex in orderedSourceIndices)
        {
            var field = _allFields[sourceFieldIndex];

            var saved = savedByIndex.TryGetValue(sourceFieldIndex, out var hit) ? hit : null;
            var row = new PlotTitleFieldEditRecord
            {
                SourceFieldIndex = sourceFieldIndex,
                Enabled = saved?.Enabled ?? false,
                Header = field.Header ?? string.Empty,
                AbbreviatedHeader = saved?.AbbreviatedHeaderOverride ?? string.Empty,
                AddLineBreakAfter = saved?.AddLineBreakAfter ?? true,
                WordWrap = saved?.WordWrap ?? true
            };
            _rows.Add(row);
            TitleFieldsGrid.Records.Add(row);
        }
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _ = TitleFieldsGrid.TryMoveSelectedRecordsStep(-1);
    }

    private void MoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _ = TitleFieldsGrid.TryMoveSelectedRecordsStep(1);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!TryBuildResult(out var result))
        {
            return;
        }

        Result = result;
        DialogResult = true;
        Close();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!TryBuildResult(out var result))
        {
            return;
        }

        Result = result;
        _previewApply?.Invoke(result);
    }

    private bool TryBuildResult(out PlotFieldDialogResult result)
    {
        result = default!;
        var label = GetGeneralText(PlotGeneralSettingKind.Label);
        var titleSegments = TitleFieldsGrid.Records
            .OfType<PlotTitleFieldEditRecord>()
            .Select(r => new PlotTitleSegmentConfiguration
            {
                SourceFieldIndex = r.SourceFieldIndex,
                Enabled = r.Enabled,
                AbbreviatedHeaderOverride = r.AbbreviatedHeader ?? string.Empty,
                AddLineBreakAfter = r.AddLineBreakAfter,
                WordWrap = r.WordWrap
            })
            .ToList();

        result = new PlotFieldDialogResult(
            TitleSelection: string.Empty,
            ShowTitle: GetSpecificBool(PlotSpecificSettingKind.ShowTitle),
            Label: label,
            TitleSegments: titleSegments,
            AxisFontSize: GetSpecificDouble(PlotSpecificSettingKind.AxisFontSize, _initial.AxisFontSize, 6d, 96d),
            TitleFontSize: GetSpecificDouble(PlotSpecificSettingKind.TitleFontSize, _initial.TitleFontSize, 6d, 120d),
            ShowXAxis: GetSpecificBool(PlotSpecificSettingKind.ShowXAxis),
            ShowYAxis: GetSpecificBool(PlotSpecificSettingKind.ShowYAxis),
            ShowXAxisTitle: GetSpecificBool(PlotSpecificSettingKind.ShowXAxisTitle),
            ShowYAxisTitle: GetSpecificBool(PlotSpecificSettingKind.ShowYAxisTitle),
            XAxisTitle: GetSpecificText(PlotSpecificSettingKind.XAxisTitle),
            YAxisTitle: GetSpecificText(PlotSpecificSettingKind.YAxisTitle),
            XAxisLabelPrecision: Math.Clamp(_initial.XAxisLabelPrecision, 0, 10),
            YAxisLabelPrecision: Math.Clamp(_initial.YAxisLabelPrecision, 0, 10),
            XAxisLabelFormat: GetSpecificText(PlotSpecificSettingKind.XAxisFormat),
            YAxisLabelFormat: GetSpecificText(PlotSpecificSettingKind.YAxisFormat),
            ChromatogramShowPeaks: GetSpecificBool(PlotSpecificSettingKind.ChromatogramShowPeaks),
            CalibrationShowRegression: GetSpecificBool(PlotSpecificSettingKind.CalibrationShowRegression),
            SpectrumNormalizeIntensity: GetSpecificBool(PlotSpecificSettingKind.SpectrumNormalizeIntensity));
        return true;
    }

    private string GetGeneralText(PlotGeneralSettingKind kind)
    {
        return GeneralGrid.Records
            .OfType<PlotGeneralSettingRecord>()
            .FirstOrDefault(r => r.Setting == kind)?
            .Value ?? string.Empty;
    }

    private bool GetSpecificBool(PlotSpecificSettingKind kind)
    {
        return SpecificGrid.Records
            .OfType<PlotSpecificSettingRecord>()
            .FirstOrDefault(r => r.Setting == kind)?
            .BoolValue ?? false;
    }

    private string GetSpecificText(PlotSpecificSettingKind kind)
    {
        return SpecificGrid.Records
            .OfType<PlotSpecificSettingRecord>()
            .FirstOrDefault(r => r.Setting == kind)?
            .TextValue ?? string.Empty;
    }

    private double GetSpecificDouble(PlotSpecificSettingKind kind, double fallback, double min, double max)
    {
        var raw = GetSpecificText(kind);
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed) ||
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            return Math.Clamp(parsed, min, max);
        }

        return Math.Clamp(fallback, min, max);
    }

    private void BuildTitleFieldGridFields()
    {
        TitleFieldsGrid.Fields.Clear();
        TitleFieldsGrid.Fields.Add(new GriddoBoolFieldView(
            "Use",
            60,
            r => ((PlotTitleFieldEditRecord)r).Enabled,
            (r, v) =>
            {
                if (v is not bool b)
                {
                    return false;
                }

                ((PlotTitleFieldEditRecord)r).Enabled = b;
                return true;
            }));
        TitleFieldsGrid.Fields.Add(new GriddoBoolFieldView(
            "Line break",
            100,
            r => ((PlotTitleFieldEditRecord)r).AddLineBreakAfter,
            (r, v) =>
            {
                if (v is not bool b)
                {
                    return false;
                }

                ((PlotTitleFieldEditRecord)r).AddLineBreakAfter = b;
                return true;
            }));
        TitleFieldsGrid.Fields.Add(new GriddoFieldView(
            "Header",
            220,
            r => ((PlotTitleFieldEditRecord)r).Header,
            static (_, _) => false,
            GriddoCellEditors.Text));
        TitleFieldsGrid.Fields.Add(new GriddoFieldView(
            "Abbr",
            140,
            r => ((PlotTitleFieldEditRecord)r).AbbreviatedHeader,
            (r, v) =>
            {
                ((PlotTitleFieldEditRecord)r).AbbreviatedHeader = v?.ToString() ?? string.Empty;
                return true;
            },
            GriddoCellEditors.Text));
        TitleFieldsGrid.Fields.Add(new GriddoBoolFieldView(
            "Wrap",
            70,
            r => ((PlotTitleFieldEditRecord)r).WordWrap,
            (r, v) =>
            {
                if (v is not bool b)
                {
                    return false;
                }

                ((PlotTitleFieldEditRecord)r).WordWrap = b;
                return true;
            }));
    }

    private void BuildGeneralGridFields()
    {
        GeneralGrid.Fields.Clear();
        GeneralGrid.Fields.Add(new GriddoFieldView(
            "Setting",
            220,
            r => ((PlotGeneralSettingRecord)r).DisplayName,
            static (_, _) => false,
            GriddoCellEditors.Text));
        GeneralGrid.Fields.Add(new GriddoFieldView(
            "Value",
            360,
            r => ((PlotGeneralSettingRecord)r).Value,
            (r, v) =>
            {
                ((PlotGeneralSettingRecord)r).Value = v?.ToString() ?? string.Empty;
                return true;
            },
            GriddoCellEditors.Text));
    }

    private void BuildSpecificGridFields()
    {
        SpecificGrid.Fields.Clear();
        SpecificGrid.Fields.Add(new GriddoFieldView(
            "Category",
            160,
            r => ((PlotSpecificSettingRecord)r).Category,
            static (_, _) => false,
            GriddoCellEditors.Text));
        SpecificGrid.Fields.Add(new GriddoFieldView(
            "Property",
            220,
            r => ((PlotSpecificSettingRecord)r).Property,
            static (_, _) => false,
            GriddoCellEditors.Text));
        SpecificGrid.Fields.Add(new PlotSpecificValueField());
    }

    private void SeedSpecificRows()
    {
        SpecificGrid.Records.Clear();
        SpecificGrid.Records.Add(new PlotSpecificSettingRecord(
            PlotSpecificSettingKind.ShowTitle, "Title", "Show title", boolValue: _initial.ShowTitle));
        SpecificGrid.Records.Add(new PlotSpecificSettingRecord(
            PlotSpecificSettingKind.TitleFontSize, "Title", "Font size", textValue: _initial.TitleFontSize.ToString("0.##", CultureInfo.InvariantCulture)));
        if (_initial.PlotTypeKey == "Chromatogram")
        {
            SpecificGrid.Records.Add(new PlotSpecificSettingRecord(
                PlotSpecificSettingKind.ChromatogramShowPeaks, "Type", "Show peak overlay", boolValue: _initial.ChromatogramShowPeaks));
        }
        else if (_initial.PlotTypeKey == "Calibration curve")
        {
            SpecificGrid.Records.Add(new PlotSpecificSettingRecord(
                PlotSpecificSettingKind.CalibrationShowRegression, "Type", "Show regression details", boolValue: _initial.CalibrationShowRegression));
        }
        else if (_initial.PlotTypeKey == "Spectrum")
        {
            SpecificGrid.Records.Add(new PlotSpecificSettingRecord(
                PlotSpecificSettingKind.SpectrumNormalizeIntensity, "Type", "Normalize intensity", boolValue: _initial.SpectrumNormalizeIntensity));
        }

        SpecificGrid.Records.Add(new PlotSpecificSettingRecord(
            PlotSpecificSettingKind.ShowXAxis, "X axis", "Show axis", boolValue: _initial.ShowXAxis));
        SpecificGrid.Records.Add(new PlotSpecificSettingRecord(
            PlotSpecificSettingKind.ShowXAxisTitle, "X axis", "Show title", boolValue: _initial.ShowXAxisTitle));
        SpecificGrid.Records.Add(new PlotSpecificSettingRecord(
            PlotSpecificSettingKind.XAxisTitle, "X axis", "Title", textValue: _initial.XAxisTitle));
        SpecificGrid.Records.Add(new PlotSpecificSettingRecord(
            PlotSpecificSettingKind.XAxisFormat, "X axis", "Format", textValue: _initial.XAxisLabelFormat));

        SpecificGrid.Records.Add(new PlotSpecificSettingRecord(
            PlotSpecificSettingKind.ShowYAxis, "Y axis", "Show axis", boolValue: _initial.ShowYAxis));
        SpecificGrid.Records.Add(new PlotSpecificSettingRecord(
            PlotSpecificSettingKind.ShowYAxisTitle, "Y axis", "Show title", boolValue: _initial.ShowYAxisTitle));
        SpecificGrid.Records.Add(new PlotSpecificSettingRecord(
            PlotSpecificSettingKind.YAxisTitle, "Y axis", "Title", textValue: _initial.YAxisTitle));
        SpecificGrid.Records.Add(new PlotSpecificSettingRecord(
            PlotSpecificSettingKind.YAxisFormat, "Y axis", "Format", textValue: _initial.YAxisLabelFormat));
        SpecificGrid.Records.Add(new PlotSpecificSettingRecord(
            PlotSpecificSettingKind.AxisFontSize, "Axes", "Font size", textValue: _initial.AxisFontSize.ToString("0.##", CultureInfo.InvariantCulture)));
    }

    private enum PlotGeneralSettingKind
    {
        Label
    }

    private enum PlotSpecificSettingKind
    {
        ChromatogramShowPeaks,
        CalibrationShowRegression,
        SpectrumNormalizeIntensity,
        ShowTitle,
        ShowXAxis,
        ShowYAxis,
        ShowXAxisTitle,
        ShowYAxisTitle,
        XAxisTitle,
        YAxisTitle,
        XAxisFormat,
        YAxisFormat,
        AxisFontSize,
        TitleFontSize
    }

    private sealed class PlotGeneralSettingRecord
    {
        public PlotGeneralSettingRecord(PlotGeneralSettingKind setting, string value)
        {
            Setting = setting;
            Value = value;
        }

        public PlotGeneralSettingKind Setting { get; }
        public string Value { get; set; } = string.Empty;

        public string DisplayName => Setting switch
        {
            PlotGeneralSettingKind.Label => "Plot label",
            _ => Setting.ToString()
        };
    }

    private sealed class PlotTitleFieldEditRecord
    {
        public int SourceFieldIndex { get; set; }
        public bool Enabled { get; set; }
        public string Header { get; set; } = string.Empty;
        public string AbbreviatedHeader { get; set; } = string.Empty;
        public bool AddLineBreakAfter { get; set; } = true;
        public bool WordWrap { get; set; } = true;
    }

    private sealed class PlotSpecificSettingRecord
    {
        public PlotSpecificSettingRecord(
            PlotSpecificSettingKind setting,
            string category,
            string property,
            bool boolValue = false,
            string textValue = "")
        {
            Setting = setting;
            Category = category;
            Property = property;
            BoolValue = boolValue;
            TextValue = textValue;
        }

        public PlotSpecificSettingKind Setting { get; }
        public string Category { get; }
        public string Property { get; }
        public bool BoolValue { get; set; }
        public string TextValue { get; set; } = string.Empty;
        public bool IsBooleanSetting => Setting is
            PlotSpecificSettingKind.ChromatogramShowPeaks or
            PlotSpecificSettingKind.CalibrationShowRegression or
            PlotSpecificSettingKind.SpectrumNormalizeIntensity or
            PlotSpecificSettingKind.ShowTitle or
            PlotSpecificSettingKind.ShowXAxis or
            PlotSpecificSettingKind.ShowYAxis or
            PlotSpecificSettingKind.ShowXAxisTitle or
            PlotSpecificSettingKind.ShowYAxisTitle;
    }

    private sealed class PlotSpecificValueField : IGriddoFieldView, IGriddoCheckboxToggleFieldView
    {
        private static readonly IGriddoCellEditor ContextualEditor = new PlotSpecificContextualValueEditor();
        public string Header { get; set; } = "Field";
        public double Width => 220;
        public bool Fill { get; set; }
        public bool IsHtml => false;
        public TextAlignment ContentAlignment => TextAlignment.Left;
        public IGriddoCellEditor Editor => ContextualEditor;

        public bool IsCheckboxCell(object recordSource)
            => recordSource is PlotSpecificSettingRecord r && r.IsBooleanSetting;

        public object? GetValue(object recordSource)
        {
            if (recordSource is not PlotSpecificSettingRecord r)
            {
                return string.Empty;
            }

            return r.IsBooleanSetting ? r.BoolValue : r.TextValue;
        }

        public bool TrySetValue(object recordSource, object? value)
        {
            if (recordSource is not PlotSpecificSettingRecord r)
            {
                return false;
            }

            if (r.IsBooleanSetting)
            {
                if (value is bool b)
                {
                    r.BoolValue = b;
                    return true;
                }

                return false;
            }

            r.TextValue = value?.ToString() ?? string.Empty;
            return true;
        }

        public string FormatValue(object? value) => value?.ToString() ?? string.Empty;
    }

    private sealed class PlotSpecificContextualValueEditor : IGriddoContextualOptionsCellEditor
    {
        private static readonly IGriddoOptionsCellEditor FormatEditor = (IGriddoOptionsCellEditor)GriddoCellEditors.FormatStringOptions;
        private static readonly string[] EmptyOptions = [];

        public IReadOnlyList<string> Options => FormatEditor.Options;
        public bool AllowMultiple => FormatEditor.AllowMultiple;

        public bool CanStartWith(char inputChar) => !char.IsControl(inputChar);

        public string BeginEdit(object? currentValue, char? firstCharacter = null)
        {
            if (firstCharacter.HasValue && !char.IsControl(firstCharacter.Value))
            {
                return firstCharacter.Value.ToString();
            }

            return currentValue?.ToString() ?? string.Empty;
        }

        public bool TryCommit(string editBuffer, out object? newValue)
        {
            newValue = editBuffer ?? string.Empty;
            return true;
        }

        public IReadOnlyList<string> ParseValues(string editBuffer) => FormatEditor.ParseValues(editBuffer);

        public string FormatValues(IEnumerable<string> values) => FormatEditor.FormatValues(values);

        public IReadOnlyList<string> GetOptions(object? recordSource)
        {
            if (recordSource is not PlotSpecificSettingRecord r)
            {
                return EmptyOptions;
            }

            return r.Setting is PlotSpecificSettingKind.XAxisFormat or PlotSpecificSettingKind.YAxisFormat
                ? FormatEditor.Options
                : EmptyOptions;
        }

        public bool TryGetOptionExample(object? recordSource, string option, out string example)
        {
            if (FormatEditor is IGriddoContextualOptionsCellEditor contextual)
            {
                return contextual.TryGetOptionExample(recordSource, option, out example);
            }

            example = string.Empty;
            return false;
        }
    }
}

public sealed record PlotFieldDialogResult(
    string TitleSelection,
    bool ShowTitle,
    string Label,
    List<PlotTitleSegmentConfiguration> TitleSegments,
    double AxisFontSize,
    double TitleFontSize,
    bool ShowXAxis,
    bool ShowYAxis,
    bool ShowXAxisTitle,
    bool ShowYAxisTitle,
    string XAxisTitle,
    string YAxisTitle,
    int XAxisLabelPrecision,
    int YAxisLabelPrecision,
    string XAxisLabelFormat,
    string YAxisLabelFormat,
    bool ChromatogramShowPeaks,
    bool CalibrationShowRegression,
    bool SpectrumNormalizeIntensity);
