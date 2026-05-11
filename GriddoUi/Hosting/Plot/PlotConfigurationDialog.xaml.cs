using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Griddo.Fields;
using Griddo.Primitives;
using Griddo.Editing;
using Griddo.Hosting.Configuration;
using Griddo.Hosting.Html;
using Griddo.Hosting.Plot;

namespace GriddoUi.Hosting.Plot;

public partial class PlotConfigurationDialog : Window
{
    private readonly IPlotFieldLayoutTarget _initial;
    private readonly IReadOnlyList<IGriddoFieldView> _allFields;
    private readonly IReadOnlyList<IGriddoFieldView> _pointLabelFields;
    private readonly List<PlotTitleFieldEditRecord> _rows = [];
    private readonly Action<PlotFieldDialogResult>? _previewApply;

    public PlotFieldDialogResult? Result { get; private set; }

    public PlotConfigurationDialog(
        IPlotFieldLayoutTarget initial,
        IReadOnlyList<IGriddoFieldView> allFields,
        Action<PlotFieldDialogResult>? previewApply = null,
        IReadOnlyList<IGriddoFieldView>? pointLabelFields = null)
    {
        InitializeComponent();
        _initial = initial;
        _allFields = allFields;
        _pointLabelFields = pointLabelFields ?? allFields;
        _previewApply = previewApply;
        BuildTitleFieldGridFields();
        BuildPointLabelFieldGridFields();
        BuildGeneralGridFields();
        BuildSpecificGridFields();
        Loaded += (_, _) =>
        {
            if (_initial.PlotTypeKey == "Calibration curve")
            {
                PointLabelsTab.Visibility = Visibility.Visible;
            }

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
        var idx = MainTabs.SelectedIndex;
        var cal = _initial.PlotTypeKey == "Calibration curve";
        var show = idx == 0 || (cal && idx == 3);
        var visibility = show ? Visibility.Visible : Visibility.Collapsed;
        MoveUpButton.Visibility = visibility;
        MoveDownButton.Visibility = visibility;
    }

    private void LoadFromChart()
    {
        SeedTitleRows();
        SeedPointLabelRows();
        GeneralGrid.Records.Clear();
        GeneralGrid.Records.Add(new PlotGeneralSettingRecord(PlotGeneralSettingKind.Label, _initial.Label));
        SeedSpecificRows();
    }

    private void SeedTitleRows()
    {
        _rows.Clear();
        TitleFieldsGrid.Records.Clear();
        var savedByKey = _initial.TitleSegments
            .Where(s => !string.IsNullOrWhiteSpace(s.SourceFieldKey))
            .GroupBy(s => s.SourceFieldKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var savedByIndex = _initial.TitleSegments
            .GroupBy(s => s.SourceFieldIndex)
            .ToDictionary(g => g.Key, g => g.First());
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
            .Select(ResolveSourceFieldIndex)
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

            var sourceFieldKey = ResolveSourceFieldKey(sourceFieldIndex);
            var saved =
                (!string.IsNullOrWhiteSpace(sourceFieldKey) && savedByKey.TryGetValue(sourceFieldKey, out var byKey))
                    ? byKey
                    : (savedByIndex.TryGetValue(sourceFieldIndex, out var byIndex) ? byIndex : null);
            var row = new PlotTitleFieldEditRecord
            {
                SourceFieldIndex = sourceFieldIndex,
                SourceFieldKey = sourceFieldKey,
                Enabled = saved?.Enabled ?? false,
                Header = field.Header ?? string.Empty,
                AbbreviatedHeader = saved?.AbbreviatedHeaderOverride ?? string.Empty,
                AddLineBreakAfter = saved?.AddLineBreakAfter ?? true,
                WordWrap = saved?.WordWrap ?? true
            };
            _rows.Add(row);
            TitleFieldsGrid.Records.Add(row);
        }

        Dispatcher.BeginInvoke(new Action(SelectEnabledTitleFieldRowsAndScrollFirst), DispatcherPriority.Loaded);
    }

    private void SeedPointLabelRows()
    {
        PointLabelFieldsGrid.Records.Clear();
        var savedByKey = _initial.CalibrationPointLabelSegments
            .Where(s => !string.IsNullOrWhiteSpace(s.SourceFieldKey))
            .GroupBy(s => s.SourceFieldKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var savedByIndex = _initial.CalibrationPointLabelSegments
            .GroupBy(s => s.SourceFieldIndex)
            .ToDictionary(g => g.Key, g => g.First());

        var configuredOrder = _initial.CalibrationPointLabelSegments
            .Select(ResolvePointLabelFieldIndex)
            .Where(i => i >= 0 && i < _pointLabelFields.Count)
            .Distinct()
            .ToList();
        var remainingOrder = Enumerable.Range(0, _pointLabelFields.Count)
            .Where(i => !configuredOrder.Contains(i))
            .ToList();
        var orderedSourceIndices = configuredOrder.Concat(remainingOrder);

        foreach (var sourceFieldIndex in orderedSourceIndices)
        {
            var field = _pointLabelFields[sourceFieldIndex];

            var sourceFieldKey = ResolvePointLabelFieldKey(sourceFieldIndex);
            var saved =
                (!string.IsNullOrWhiteSpace(sourceFieldKey) && savedByKey.TryGetValue(sourceFieldKey, out var byKey))
                    ? byKey
                    : (savedByIndex.TryGetValue(sourceFieldIndex, out var byIndex) ? byIndex : null);
            var row = new PlotTitleFieldEditRecord
            {
                SourceFieldIndex = sourceFieldIndex,
                SourceFieldKey = sourceFieldKey,
                Enabled = saved?.Enabled ?? false,
                Header = field.Header ?? string.Empty,
                AbbreviatedHeader = saved?.AbbreviatedHeaderOverride ?? string.Empty,
                AddLineBreakAfter = saved?.AddLineBreakAfter ?? true,
                WordWrap = saved?.WordWrap ?? true
            };
            PointLabelFieldsGrid.Records.Add(row);
        }
    }

    /// <summary>Select every title row with Use=true; scroll the first enabled row to the viewport center.</summary>
    private void SelectEnabledTitleFieldRowsAndScrollFirst()
    {
        var indices = new List<int>();
        for (var i = 0; i < TitleFieldsGrid.Records.Count; i++)
        {
            if (TitleFieldsGrid.Records[i] is PlotTitleFieldEditRecord { Enabled: true })
            {
                indices.Add(i);
            }
        }

        if (indices.Count == 0)
        {
            return;
        }

        TitleFieldsGrid.ClearCellSelection();
        for (var i = 0; i < indices.Count; i++)
        {
            TitleFieldsGrid.SelectEntireRecord(indices[i], additive: i > 0);
        }

        TitleFieldsGrid.CenterCellInViewport(new GriddoCellAddress(indices[0], 0));
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var grid = ActiveTitleLikeGrid();
        _ = grid?.TryMoveSelectedRecordsStep(-1);
    }

    private void MoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var grid = ActiveTitleLikeGrid();
        _ = grid?.TryMoveSelectedRecordsStep(1);
    }

    private global::Griddo.Grid.Griddo? ActiveTitleLikeGrid()
    {
        var cal = _initial.PlotTypeKey == "Calibration curve";
        return MainTabs.SelectedIndex switch
        {
            0 => TitleFieldsGrid,
            3 when cal => PointLabelFieldsGrid,
            _ => null
        };
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
            .Select(r =>
            {
                var field = r.SourceFieldIndex >= 0 && r.SourceFieldIndex < _allFields.Count
                    ? _allFields[r.SourceFieldIndex]
                    : null;
                var sourceObjectName = field is IGriddoFieldSourceObject so ? so.SourceObjectName.Trim() : string.Empty;
                var propertyName = field is IGriddoFieldSourceMember sm ? sm.SourceMemberName.Trim() : string.Empty;
                return new PlotTitleSegmentConfiguration
                {
                    SourceObjectName = sourceObjectName,
                    PropertyName = propertyName,
                    SourceFieldIndex = r.SourceFieldIndex,
                    SourceFieldKey = r.SourceFieldKey ?? string.Empty,
                    Enabled = r.Enabled,
                    AbbreviatedHeaderOverride = r.AbbreviatedHeader ?? string.Empty,
                    AddLineBreakAfter = r.AddLineBreakAfter,
                    WordWrap = r.WordWrap
                };
            })
            .ToList();

        var calibrationPointLabelSegments = PointLabelFieldsGrid.Records
            .OfType<PlotTitleFieldEditRecord>()
            .Select(r =>
            {
                var field = r.SourceFieldIndex >= 0 && r.SourceFieldIndex < _pointLabelFields.Count
                    ? _pointLabelFields[r.SourceFieldIndex]
                    : null;
                var sourceObjectName = field is IGriddoFieldSourceObject so ? so.SourceObjectName.Trim() : string.Empty;
                var propertyName = field is IGriddoFieldSourceMember sm ? sm.SourceMemberName.Trim() : string.Empty;
                return new PlotTitleSegmentConfiguration
                {
                    SourceObjectName = sourceObjectName,
                    PropertyName = propertyName,
                    SourceFieldIndex = r.SourceFieldIndex,
                    SourceFieldKey = r.SourceFieldKey ?? string.Empty,
                    Enabled = r.Enabled,
                    AbbreviatedHeaderOverride = r.AbbreviatedHeader ?? string.Empty,
                    AddLineBreakAfter = r.AddLineBreakAfter,
                    WordWrap = r.WordWrap
                };
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
            ShowCalibrationPointLabels: GetSpecificBool(PlotSpecificSettingKind.CalibrationShowPointLabels),
            CalibrationPointLabelSegments: calibrationPointLabelSegments,
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

    private int ResolveSourceFieldIndex(PlotTitleSegmentConfiguration segment)
    {
        return HostingSegmentFieldResolver.Resolve(
            _allFields,
            segment.SourceObjectName,
            segment.PropertyName,
            segment.SourceFieldKey,
            segment.SourceFieldIndex);
    }

    private int ResolvePointLabelFieldIndex(PlotTitleSegmentConfiguration segment)
    {
        return HostingSegmentFieldResolver.Resolve(
            _pointLabelFields,
            segment.SourceObjectName,
            segment.PropertyName,
            segment.SourceFieldKey,
            segment.SourceFieldIndex);
    }

    private string ResolveSourceFieldKey(int sourceFieldIndex)
    {
        if (sourceFieldIndex < 0 || sourceFieldIndex >= _allFields.Count)
        {
            return string.Empty;
        }

        var field = _allFields[sourceFieldIndex];
        if (field is IGriddoFieldSourceMember sourceMember && !string.IsNullOrWhiteSpace(sourceMember.SourceMemberName))
        {
            return sourceMember.SourceMemberName;
        }

        return !string.IsNullOrWhiteSpace(field.Header) ? field.Header : sourceFieldIndex.ToString(CultureInfo.InvariantCulture);
    }

    private string ResolvePointLabelFieldKey(int sourceFieldIndex)
    {
        if (sourceFieldIndex < 0 || sourceFieldIndex >= _pointLabelFields.Count)
        {
            return string.Empty;
        }

        var field = _pointLabelFields[sourceFieldIndex];
        if (field is IGriddoFieldSourceMember sourceMember && !string.IsNullOrWhiteSpace(sourceMember.SourceMemberName))
        {
            return sourceMember.SourceMemberName;
        }

        return !string.IsNullOrWhiteSpace(field.Header) ? field.Header : sourceFieldIndex.ToString(CultureInfo.InvariantCulture);
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

    private void BuildPointLabelFieldGridFields()
    {
        PointLabelFieldsGrid.Fields.Clear();
        PointLabelFieldsGrid.Fields.Add(new GriddoBoolFieldView(
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
        PointLabelFieldsGrid.Fields.Add(new GriddoBoolFieldView(
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
        PointLabelFieldsGrid.Fields.Add(new GriddoFieldView(
            "Header",
            220,
            r => ((PlotTitleFieldEditRecord)r).Header,
            static (_, _) => false,
            GriddoCellEditors.Text));
        PointLabelFieldsGrid.Fields.Add(new GriddoFieldView(
            "Abbr",
            140,
            r => ((PlotTitleFieldEditRecord)r).AbbreviatedHeader,
            (r, v) =>
            {
                ((PlotTitleFieldEditRecord)r).AbbreviatedHeader = v?.ToString() ?? string.Empty;
                return true;
            },
            GriddoCellEditors.Text));
        PointLabelFieldsGrid.Fields.Add(new GriddoBoolFieldView(
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
                PlotSpecificSettingKind.CalibrationShowPointLabels, "Point labels", "Show point labels", boolValue: _initial.ShowCalibrationPointLabels));
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
        CalibrationShowPointLabels,
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
        public string SourceFieldKey { get; set; } = string.Empty;
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
    bool ShowCalibrationPointLabels,
    List<PlotTitleSegmentConfiguration> CalibrationPointLabelSegments,
    bool SpectrumNormalizeIntensity);
