using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Griddo.Abstractions.Editing;
using Griddo.Abstractions.Fields;
using Griddo.Fields;
using Griddo.Primitives;
using Griddo.Editing;
using Griddo.Hosting.Configuration;
using Griddo.Hosting.Html;
using Griddo.Hosting.Plot;
using GriddoUi.FieldEdit.Models;

namespace GriddoUi.Hosting.Plot;

public partial class PlotConfigurationDialog : Window
{
    private readonly IPlotFieldLayoutTarget _initial;
    private readonly IReadOnlyList<IGriddoFieldView> _allFields;
    private readonly IReadOnlyList<IGriddoFieldView> _pointLabelFields;
    private readonly IReadOnlyList<PlotTitleSegmentConfiguration> _titleSegmentsForSeed;
    private readonly IReadOnlyList<PlotTitleSegmentConfiguration> _pointLabelSegmentsForSeed;
    private readonly List<PlotTitleFieldEditRecord> _rows = [];
    private readonly Action<PlotFieldDialogResult>? _previewApply;
    private readonly Func<IGriddoFieldView, FieldRegistrationDisplayInfo?>? _resolveRegistration;
    private readonly Action<FieldRegistrationDisplayInfo>? _persistRegistration;

    public PlotFieldDialogResult? Result { get; private set; }

    public PlotConfigurationDialog(
        IPlotFieldLayoutTarget initial,
        IReadOnlyList<IGriddoFieldView> allFields,
        Action<PlotFieldDialogResult>? previewApply = null,
        IReadOnlyList<IGriddoFieldView>? pointLabelFields = null,
        Func<IGriddoFieldView, FieldRegistrationDisplayInfo?>? resolveRegistration = null,
        Action<FieldRegistrationDisplayInfo>? persistRegistration = null,
        IReadOnlyList<PlotTitleSegmentConfiguration>? titleSegmentsForSeed = null,
        IReadOnlyList<PlotTitleSegmentConfiguration>? pointLabelSegmentsForSeed = null)
    {
        InitializeComponent();
        _initial = initial;
        _allFields = allFields;
        _pointLabelFields = pointLabelFields ?? allFields;
        _titleSegmentsForSeed = titleSegmentsForSeed ?? initial.TitleSegments ?? [];
        _pointLabelSegmentsForSeed = pointLabelSegmentsForSeed ?? initial.CalibrationPointLabelSegments ?? [];
        _previewApply = previewApply;
        _resolveRegistration = resolveRegistration;
        _persistRegistration = persistRegistration;
        BuildTitleFieldGridFields();
        BuildPointLabelFieldGridFields();
        BuildSpecificGridFields();
        Loaded += (_, _) =>
        {
            if (SupportsPointLabelTab())
            {
                LabelsTab.Visibility = Visibility.Visible;
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

    private bool SupportsPointLabelTab() =>
        _initial.PlotTypeKey is "Calibration curve" or "Chromatogram";

    private const int TitleTabIndex = 1;
    private const int LabelsTabIndex = 2;

    private void UpdateMoveButtonsVisibility()
    {
        var idx = MainTabs.SelectedIndex;
        var show = idx == TitleTabIndex || (SupportsPointLabelTab() && idx == LabelsTabIndex);
        var visibility = show ? Visibility.Visible : Visibility.Collapsed;
        MoveUpButton.Visibility = visibility;
        MoveDownButton.Visibility = visibility;
    }

    private void LoadFromChart()
    {
        SeedTitleRows();
        SeedPointLabelRows();
        SeedSpecificRows();
    }

    private void SeedTitleRows()
    {
        _rows.Clear();
        TitleFieldsGrid.Records.Clear();
        var savedByIndex = _titleSegmentsForSeed
            .Select(s => (segment: s, resolved: ResolveSourceFieldIndex(s)))
            .Where(x => x.resolved >= 0)
            .GroupBy(x => x.resolved)
            .ToDictionary(g => g.Key, g => g.First().segment);
        var excluded = new HashSet<int>();
        for (var sourceFieldIndex = 0; sourceFieldIndex < _allFields.Count; sourceFieldIndex++)
        {
            var field = _allFields[sourceFieldIndex];
            if (ReferenceEquals(field, _initial) || field is IHtmlFieldLayoutTarget || field is IPlotFieldLayoutTarget)
            {
                excluded.Add(sourceFieldIndex);
            }
        }

        var configuredOrder = _titleSegmentsForSeed
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

            var saved = savedByIndex.TryGetValue(sourceFieldIndex, out var byIndex) ? byIndex : null;
            var row = CreateSegmentEditRecord(field, sourceFieldIndex, saved);
            _rows.Add(row);
            TitleFieldsGrid.Records.Add(row);
        }

        Dispatcher.BeginInvoke(new Action(SelectEnabledTitleFieldRowsAndScrollFirst), DispatcherPriority.Loaded);
    }

    private void SeedPointLabelRows()
    {
        PointLabelFieldsGrid.Records.Clear();
        var excluded = new HashSet<int>();
        if (_initial.PlotTypeKey == "Chromatogram")
        {
            for (var sourceFieldIndex = 0; sourceFieldIndex < _pointLabelFields.Count; sourceFieldIndex++)
            {
                var field = _pointLabelFields[sourceFieldIndex];
                if (ReferenceEquals(field, _initial) || field is IHtmlFieldLayoutTarget || field is IPlotFieldLayoutTarget)
                {
                    excluded.Add(sourceFieldIndex);
                }
            }
        }

        var savedByIndex = _pointLabelSegmentsForSeed
            .Select(s => (segment: s, resolved: ResolvePointLabelFieldIndex(s)))
            .Where(x => x.resolved >= 0)
            .GroupBy(x => x.resolved)
            .ToDictionary(g => g.Key, g => g.First().segment);

        var configuredOrder = _pointLabelSegmentsForSeed
            .Select(ResolvePointLabelFieldIndex)
            .Where(i => i >= 0 && i < _pointLabelFields.Count && !excluded.Contains(i))
            .Distinct()
            .ToList();
        var remainingOrder = Enumerable.Range(0, _pointLabelFields.Count)
            .Where(i => !excluded.Contains(i) && !configuredOrder.Contains(i))
            .ToList();
        var orderedSourceIndices = configuredOrder.Concat(remainingOrder);

        foreach (var sourceFieldIndex in orderedSourceIndices)
        {
            var field = _pointLabelFields[sourceFieldIndex];

            var saved = savedByIndex.TryGetValue(sourceFieldIndex, out var byIndex) ? byIndex : null;
            PointLabelFieldsGrid.Records.Add(CreateSegmentEditRecord(field, sourceFieldIndex, saved));
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
        return MainTabs.SelectedIndex switch
        {
            TitleTabIndex => TitleFieldsGrid,
            LabelsTabIndex when SupportsPointLabelTab() => PointLabelFieldsGrid,
            _ => null
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        PersistAllRegistrationRows();
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
        PersistAllRegistrationRows();
        if (!TryBuildResult(out var result))
        {
            return;
        }

        Result = result;
        _previewApply?.Invoke(result);
    }

    private void PersistRegistrationFromRow(PlotTitleFieldEditRecord row)
    {
        if (_persistRegistration is null || string.IsNullOrWhiteSpace(row.Key))
        {
            return;
        }

        _persistRegistration(new FieldRegistrationDisplayInfo
        {
            Key = row.Key,
            Source = row.Source,
            Property = row.Property,
            LongHeader = row.LongHeader,
            ShortHeader = row.ShortHeader,
            Format = row.Format,
            Description = row.Description
        });
    }

    private void PersistAllRegistrationRows()
    {
        TitleFieldsGrid.CommitPendingCellEdit();
        PointLabelFieldsGrid.CommitPendingCellEdit();
        foreach (var row in TitleFieldsGrid.Records.OfType<PlotTitleFieldEditRecord>())
        {
            PersistRegistrationFromRow(row);
        }

        foreach (var row in PointLabelFieldsGrid.Records.OfType<PlotTitleFieldEditRecord>())
        {
            PersistRegistrationFromRow(row);
        }
    }

    private static PlotTitleSegmentConfiguration ToSegmentConfiguration(PlotTitleFieldEditRecord r) =>
        new()
        {
            SourceObjectName = r.Source.Trim(),
            PropertyName = r.Property.Trim(),
            SourceFieldIndex = r.SourceFieldIndex,
            Enabled = r.Enabled,
            AddLineBreakBefore = r.AddLineBreakBefore
        };

    /// <summary>
    /// Point-label segments must keep the label field catalog source (e.g. <c>Compound.Peak</c>),
    /// not the shorter registration source resolved via suffix lookup (e.g. <c>Peak</c>).
    /// </summary>
    private PlotTitleSegmentConfiguration ToPointLabelSegmentConfiguration(PlotTitleFieldEditRecord r)
    {
        var segment = ToSegmentConfiguration(r);
        if (r.SourceFieldIndex < 0 || r.SourceFieldIndex >= _pointLabelFields.Count)
        {
            return segment;
        }
        var field = _pointLabelFields[r.SourceFieldIndex];
        if (field is IGriddoFieldSourceObject sourceObject
            && !string.IsNullOrWhiteSpace(sourceObject.SourceObjectName))
        {
            segment.SourceObjectName = sourceObject.SourceObjectName.Trim();
        }
        if (field is IGriddoFieldSourceMember sourceMember
            && !string.IsNullOrWhiteSpace(sourceMember.SourceMemberName))
        {
            segment.PropertyName = sourceMember.SourceMemberName.Trim();
        }
        segment.SourceFieldIndex = r.SourceFieldIndex;
        return segment;
    }

    private bool TryBuildResult(out PlotFieldDialogResult result)
    {
        result = default!;
        var label = GetSpecificText(PlotSpecificSettingKind.Label);
        var titleSegments = TitleFieldsGrid.Records
            .OfType<PlotTitleFieldEditRecord>()
            .Where(r => r.Enabled)
            .Select(r =>
            {
                PersistRegistrationFromRow(r);
                return ToSegmentConfiguration(r);
            })
            .ToList();

        var calibrationPointLabelSegments = PointLabelFieldsGrid.Records
            .OfType<PlotTitleFieldEditRecord>()
            .Where(r => r.Enabled)
            .Select(r =>
            {
                PersistRegistrationFromRow(r);
                return ToPointLabelSegmentConfiguration(r);
            })
            .ToList();

        result = new PlotFieldDialogResult(
            TitleSelection: string.Empty,
            ShowTitle: GetSpecificBool(PlotSpecificSettingKind.ShowTitle),
            Label: label,
            TitleSegments: titleSegments,
            AxisFontSize: GetSpecificDouble(PlotSpecificSettingKind.AxisFontSize, _initial.AxisFontSize, 6d, 96d),
            TitleFontSize: GetSpecificDouble(PlotSpecificSettingKind.TitleFontSize, _initial.TitleFontSize, 6d, 120d),
            PeakLabelFontSize: GetSpecificDouble(PlotSpecificSettingKind.PeakLabelFontSize, _initial.PeakLabelFontSize, 6d, 96d),
            CalibrationPointLabelFontSize: GetSpecificDouble(
                PlotSpecificSettingKind.CalibrationPointLabelFontSize,
                _initial.CalibrationPointLabelFontSize,
                6d,
                96d),
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
            ChromatogramShowExpectedRtLine: _initial.PlotTypeKey == "Chromatogram"
                ? GetSpecificBool(PlotSpecificSettingKind.ChromatogramShowExpectedRtLine)
                : _initial.ChromatogramShowExpectedRtLine,
            ChromatogramShowRtLimitLines: _initial.PlotTypeKey == "Chromatogram"
                ? GetSpecificBool(PlotSpecificSettingKind.ChromatogramShowRtLimitLines)
                : _initial.ChromatogramShowRtLimitLines,
            ChromatogramShowSelectionCorrectedRtOnTic: _initial.PlotTypeKey == "Chromatogram"
                ? (_initial is HostedChromatogramFieldView { SourceMemberName: "TotalIon" }
                    ? GetSpecificBool(PlotSpecificSettingKind.ChromatogramShowSelectionCorrectedRtOnTic)
                    : _initial.ChromatogramShowSelectionCorrectedRtOnTic)
                : _initial.ChromatogramShowSelectionCorrectedRtOnTic,
            CalibrationShowRegression: GetSpecificBool(PlotSpecificSettingKind.CalibrationShowRegression),
            ShowCalibrationPointLabels: ResolveShowCalibrationPointLabels(calibrationPointLabelSegments),
            CalibrationPointLabelSegments: calibrationPointLabelSegments,
            SpectrumNormalizeIntensity: GetSpecificBool(PlotSpecificSettingKind.SpectrumNormalizeIntensity),
            PeakLabelRotate: PlotPeakLabelRotation.Normalize(
                GetSpecificInt(PlotSpecificSettingKind.PeakLabelRotate, _initial.PeakLabelRotate)));
        return true;
    }

    private bool ResolveShowCalibrationPointLabels(IReadOnlyList<PlotTitleSegmentConfiguration> segments)
    {
        if (!SupportsPointLabelTab())
        {
            return _initial.ShowCalibrationPointLabels;
        }

        if (IsChromatogramPlot())
        {
            return segments.Any(static s => s.Enabled);
        }

        return GetSpecificBool(PlotSpecificSettingKind.CalibrationShowPointLabels);
    }

    private bool GetSpecificBool(PlotSpecificSettingKind kind)
    {
        var record = SpecificGrid.Records
            .OfType<PlotSpecificSettingRecord>()
            .FirstOrDefault(r => r.Setting == kind);
        if (record is not null)
        {
            return record.BoolValue;
        }

        return kind switch
        {
            PlotSpecificSettingKind.ShowTitle => _initial.ShowTitle,
            PlotSpecificSettingKind.ShowXAxis => _initial.ShowXAxis,
            PlotSpecificSettingKind.ShowYAxis => _initial.ShowYAxis,
            PlotSpecificSettingKind.ShowXAxisTitle => _initial.ShowXAxisTitle,
            PlotSpecificSettingKind.ShowYAxisTitle => _initial.ShowYAxisTitle,
            PlotSpecificSettingKind.ChromatogramShowPeaks => _initial.ChromatogramShowPeaks,
            PlotSpecificSettingKind.ChromatogramShowExpectedRtLine => _initial.ChromatogramShowExpectedRtLine,
            PlotSpecificSettingKind.ChromatogramShowRtLimitLines => _initial.ChromatogramShowRtLimitLines,
            PlotSpecificSettingKind.ChromatogramShowSelectionCorrectedRtOnTic => _initial.ChromatogramShowSelectionCorrectedRtOnTic,
            PlotSpecificSettingKind.CalibrationShowRegression => _initial.CalibrationShowRegression,
            PlotSpecificSettingKind.CalibrationShowPointLabels => _initial.ShowCalibrationPointLabels,
            PlotSpecificSettingKind.SpectrumNormalizeIntensity => _initial.SpectrumNormalizeIntensity,
            _ => false,
        };
    }

    private string GetSpecificText(PlotSpecificSettingKind kind)
    {
        var record = SpecificGrid.Records
            .OfType<PlotSpecificSettingRecord>()
            .FirstOrDefault(r => r.Setting == kind);
        if (record is not null)
        {
            return record.TextValue;
        }

        return kind switch
        {
            PlotSpecificSettingKind.Label => _initial.Label,
            PlotSpecificSettingKind.XAxisTitle => _initial.XAxisTitle,
            PlotSpecificSettingKind.YAxisTitle => _initial.YAxisTitle,
            PlotSpecificSettingKind.XAxisFormat => _initial.XAxisLabelFormat,
            PlotSpecificSettingKind.YAxisFormat => _initial.YAxisLabelFormat,
            PlotSpecificSettingKind.TitleFontSize => _initial.TitleFontSize.ToString("0.##", CultureInfo.InvariantCulture),
            PlotSpecificSettingKind.AxisFontSize => _initial.AxisFontSize.ToString("0.##", CultureInfo.InvariantCulture),
            PlotSpecificSettingKind.PeakLabelFontSize => _initial.PeakLabelFontSize.ToString("0.##", CultureInfo.InvariantCulture),
            PlotSpecificSettingKind.CalibrationPointLabelFontSize => _initial.CalibrationPointLabelFontSize.ToString("0.##", CultureInfo.InvariantCulture),
            PlotSpecificSettingKind.PeakLabelRotate => _initial.PeakLabelRotate.ToString(CultureInfo.InvariantCulture),
            _ => string.Empty,
        };
    }

    private int GetSpecificInt(PlotSpecificSettingKind kind, int fallback)
    {
        var text = GetSpecificText(kind);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
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
            segment.SourceFieldIndex);
    }

    private int ResolvePointLabelFieldIndex(PlotTitleSegmentConfiguration segment)
    {
        return HostingSegmentFieldResolver.Resolve(
            _pointLabelFields,
            segment.SourceObjectName,
            segment.PropertyName,
            segment.SourceFieldIndex);
    }

    private void BuildTitleFieldGridFields() => BuildSegmentGridFields(TitleFieldsGrid);

    private void BuildPointLabelFieldGridFields() => BuildSegmentGridFields(PointLabelFieldsGrid);

    private void BuildSegmentGridFields(global::Griddo.Grid.Griddo grid)
    {
        grid.Fields.Clear();
        grid.Fields.Add(new GriddoBoolFieldView(
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
            })
        {
            Description = "Use: include this segment in this plot field only (saved per plot, not in the central field repository)"
        });
        grid.Fields.Add(new GriddoBoolFieldView(
            "Line break",
            100,
            r => ((PlotTitleFieldEditRecord)r).AddLineBreakBefore,
            (r, v) =>
            {
                if (v is not bool b)
                {
                    return false;
                }

                ((PlotTitleFieldEditRecord)r).AddLineBreakBefore = b;
                return true;
            })
        {
            Description = "Line break before this segment (saved per plot field only, not in the central repository)"
        });
        grid.Fields.Add(new GriddoFieldView(
            "Key",
            220,
            r => ((PlotTitleFieldEditRecord)r).Key,
            static (_, _) => false,
            GriddoCellEditors.Text)
        {
            Description = "Unique registration key (Source.Property), same as the Field editor"
        });
        grid.Fields.Add(new GriddoFieldView(
            "Source",
            160,
            r => ((PlotTitleFieldEditRecord)r).Source,
            static (_, _) => false,
            GriddoCellEditors.Text)
        {
            Description = "Source object name from the central field registration"
        });
        grid.Fields.Add(new GriddoFieldView(
            "Property",
            120,
            r => ((PlotTitleFieldEditRecord)r).Property,
            static (_, _) => false,
            GriddoCellEditors.Text)
        {
            Description = "Property name from the central field registration"
        });
        grid.Fields.Add(new GriddoFieldView(
            "Long Header",
            140,
            r => ((PlotTitleFieldEditRecord)r).LongHeader,
            (r, v) =>
            {
                var row = (PlotTitleFieldEditRecord)r;
                row.LongHeader = v?.ToString() ?? string.Empty;
                PersistRegistrationFromRow(row);
                return true;
            },
            GriddoCellEditors.Text)
        {
            Description = "Long header for data grid column headers (central repository)"
        });
        grid.Fields.Add(new GriddoFieldView(
            "Short Header",
            100,
            r => ((PlotTitleFieldEditRecord)r).ShortHeader,
            (r, v) =>
            {
                var row = (PlotTitleFieldEditRecord)r;
                row.ShortHeader = v?.ToString() ?? string.Empty;
                PersistRegistrationFromRow(row);
                return true;
            },
            GriddoCellEditors.Text)
        {
            Description = "Short header for plot title and label segments (central repository)"
        });
        grid.Fields.Add(new GriddoFieldView(
            "Format",
            80,
            r => ((PlotTitleFieldEditRecord)r).Format,
            (r, v) =>
            {
                var row = (PlotTitleFieldEditRecord)r;
                row.Format = v?.ToString() ?? string.Empty;
                PersistRegistrationFromRow(row);
                return true;
            },
            GriddoCellEditors.FormatStringOptions)
        {
            Description = "Format reference: general format name or literal (central repository)"
        });
        grid.Fields.Add(new GriddoFieldView(
            "Description",
            260,
            r => ((PlotTitleFieldEditRecord)r).Description,
            (r, v) =>
            {
                var row = (PlotTitleFieldEditRecord)r;
                row.Description = v?.ToString() ?? string.Empty;
                PersistRegistrationFromRow(row);
                return true;
            },
            GriddoCellEditors.Text)
        {
            Description = "Field description / tooltip (central repository)"
        });
    }

    private PlotTitleFieldEditRecord CreateSegmentEditRecord(
        IGriddoFieldView field,
        int sourceFieldIndex,
        PlotTitleSegmentConfiguration? saved)
    {
        var reg = _resolveRegistration?.Invoke(field);
        var row = new PlotTitleFieldEditRecord
        {
            SourceFieldIndex = sourceFieldIndex,
            Enabled = saved?.Enabled ?? false,
            Key = reg?.Key ?? string.Empty,
            Source = reg?.Source ?? (field is IGriddoFieldSourceObject so ? so.SourceObjectName : string.Empty),
            Property = reg?.Property ?? (field is IGriddoFieldSourceMember sm ? sm.SourceMemberName : string.Empty),
            LongHeader = reg?.LongHeader ?? string.Empty,
            ShortHeader = reg?.ShortHeader ?? string.Empty,
            Format = reg?.Format ?? string.Empty,
            Description = reg?.Description ?? string.Empty,
            AddLineBreakBefore = saved?.AddLineBreakBefore ?? false
        };
        if (string.IsNullOrWhiteSpace(row.Key)
            && !string.IsNullOrWhiteSpace(row.Source)
            && !string.IsNullOrWhiteSpace(row.Property))
        {
            row.Key = $"{row.Source}.{row.Property}";
        }

        return row;
    }

    private void BuildSpecificGridFields()
    {
        SpecificGrid.Fields.Clear();
        SpecificGrid.Fields.Add(new GriddoFieldView(
            "Category",
            160,
            r => ((PlotSpecificSettingRecord)r).Category,
            static (_, _) => false,
            GriddoCellEditors.Text)
        {
            Description = "Setting category"
        });
        SpecificGrid.Fields.Add(new GriddoFieldView(
            "Property",
            220,
            r => ((PlotSpecificSettingRecord)r).Property,
            static (_, _) => false,
            GriddoCellEditors.Text)
        {
            Description = "Plot-specific setting name"
        });
        SpecificGrid.Fields.Add(new PlotSpecificValueField());
    }

    private bool IsChromatogramPlot() => _initial.PlotTypeKey == "Chromatogram";

    private bool IsSampleTicPlot() =>
        _initial is HostedChromatogramFieldView chrom
        && HostedChromatogramFieldView.IsSampleTicPlot(chrom.SourceMemberName);

    private bool IncludesPlotSpecificSetting(PlotSpecificSettingKind kind)
    {
        if (IsChromatogramPlot() && kind is
            PlotSpecificSettingKind.Label
            or PlotSpecificSettingKind.ShowTitle
            or PlotSpecificSettingKind.CalibrationShowPointLabels)
        {
            return false;
        }

        if (IsSampleTicPlot() && kind is
            PlotSpecificSettingKind.ChromatogramShowExpectedRtLine
            or PlotSpecificSettingKind.ChromatogramShowRtLimitLines
            or PlotSpecificSettingKind.XAxisFormat
            or PlotSpecificSettingKind.YAxisFormat)
        {
            return false;
        }

        return true;
    }

    private void AddPlotSpecificSetting(PlotSpecificSettingRecord record)
    {
        if (IncludesPlotSpecificSetting(record.Setting))
        {
            SpecificGrid.Records.Add(record);
        }
    }

    private void SeedSpecificRows()
    {
        SpecificGrid.Records.Clear();
        AddPlotSpecificSetting(new PlotSpecificSettingRecord(
            PlotSpecificSettingKind.Label, "General", "Plot label", textValue: _initial.Label));
        AddPlotSpecificSetting(new PlotSpecificSettingRecord(
            PlotSpecificSettingKind.ShowTitle, "Title", "Show title", boolValue: _initial.ShowTitle));
        AddPlotSpecificSetting(new PlotSpecificSettingRecord(
            PlotSpecificSettingKind.TitleFontSize, "Title", "Font size", textValue: _initial.TitleFontSize.ToString("0.##", CultureInfo.InvariantCulture)));
        if (_initial.PlotTypeKey == "Chromatogram")
        {
            AddPlotSpecificSetting(new PlotSpecificSettingRecord(
                PlotSpecificSettingKind.CalibrationShowPointLabels,
                "Peak labels",
                "Show peak labels and connector lines",
                boolValue: _initial.ShowCalibrationPointLabels));
            AddPlotSpecificSetting(new PlotSpecificSettingRecord(
                PlotSpecificSettingKind.PeakLabelFontSize,
                "Peak labels",
                "Font size",
                textValue: _initial.PeakLabelFontSize.ToString("0.##", CultureInfo.InvariantCulture)));
            AddPlotSpecificSetting(new PlotSpecificSettingRecord(
                PlotSpecificSettingKind.ChromatogramShowPeaks, "Type", "Show peak fill", boolValue: _initial.ChromatogramShowPeaks));
            AddPlotSpecificSetting(new PlotSpecificSettingRecord(
                PlotSpecificSettingKind.ChromatogramShowExpectedRtLine, "Markers", "Show expected RT line (corrected)", boolValue: _initial.ChromatogramShowExpectedRtLine));
            AddPlotSpecificSetting(new PlotSpecificSettingRecord(
                PlotSpecificSettingKind.ChromatogramShowRtLimitLines, "Markers", "Show RT limit lines (±TimeWindow/2 on corrected RT)", boolValue: _initial.ChromatogramShowRtLimitLines));
            if (_initial is HostedChromatogramFieldView { SourceMemberName: var chromMember }
                && HostedChromatogramFieldView.UsesFixedPeakLabelOverlayDrawing(chromMember))
            {
                AddPlotSpecificSetting(new PlotSpecificSettingRecord(
                    PlotSpecificSettingKind.PeakLabelRotate,
                    "General",
                    "Rotate labels (°)",
                    textValue: _initial.PeakLabelRotate.ToString(CultureInfo.InvariantCulture)));
            }

            if (_initial is HostedChromatogramFieldView { SourceMemberName: "TotalIon" })
            {
                AddPlotSpecificSetting(new PlotSpecificSettingRecord(
                    PlotSpecificSettingKind.ChromatogramShowSelectionCorrectedRtOnTic,
                    "Markers",
                    "Show compound-selection corrected RT on TIC",
                    boolValue: _initial.ChromatogramShowSelectionCorrectedRtOnTic));
            }
        }
        else if (_initial.PlotTypeKey == "Calibration curve")
        {
            SpecificGrid.Records.Add(new PlotSpecificSettingRecord(
                PlotSpecificSettingKind.CalibrationShowPointLabels,
                "Labels",
                "Show labels and connector lines",
                boolValue: _initial.ShowCalibrationPointLabels));
            SpecificGrid.Records.Add(new PlotSpecificSettingRecord(
                PlotSpecificSettingKind.CalibrationPointLabelFontSize,
                "Labels",
                "Font size",
                textValue: _initial.CalibrationPointLabelFontSize.ToString("0.##", CultureInfo.InvariantCulture)));
            SpecificGrid.Records.Add(new PlotSpecificSettingRecord(
                PlotSpecificSettingKind.CalibrationShowRegression, "Type", "Show regression details", boolValue: _initial.CalibrationShowRegression));
        }
        else if (_initial.PlotTypeKey == "Spectrum")
        {
            SpecificGrid.Records.Add(new PlotSpecificSettingRecord(
                PlotSpecificSettingKind.SpectrumNormalizeIntensity, "Type", "Normalize intensity", boolValue: _initial.SpectrumNormalizeIntensity));
        }

        AddPlotSpecificSetting(new PlotSpecificSettingRecord(
            PlotSpecificSettingKind.ShowXAxis, "X axis", "Show axis", boolValue: _initial.ShowXAxis));
        AddPlotSpecificSetting(new PlotSpecificSettingRecord(
            PlotSpecificSettingKind.ShowXAxisTitle, "X axis", "Show title", boolValue: _initial.ShowXAxisTitle));
        AddPlotSpecificSetting(new PlotSpecificSettingRecord(
            PlotSpecificSettingKind.XAxisTitle, "X axis", "Title", textValue: _initial.XAxisTitle));
        AddPlotSpecificSetting(new PlotSpecificSettingRecord(
            PlotSpecificSettingKind.XAxisFormat, "X axis", "Format", textValue: _initial.XAxisLabelFormat));

        AddPlotSpecificSetting(new PlotSpecificSettingRecord(
            PlotSpecificSettingKind.ShowYAxis, "Y axis", "Show axis", boolValue: _initial.ShowYAxis));
        AddPlotSpecificSetting(new PlotSpecificSettingRecord(
            PlotSpecificSettingKind.ShowYAxisTitle, "Y axis", "Show title", boolValue: _initial.ShowYAxisTitle));
        AddPlotSpecificSetting(new PlotSpecificSettingRecord(
            PlotSpecificSettingKind.YAxisTitle, "Y axis", "Title", textValue: _initial.YAxisTitle));
        AddPlotSpecificSetting(new PlotSpecificSettingRecord(
            PlotSpecificSettingKind.YAxisFormat, "Y axis", "Format", textValue: _initial.YAxisLabelFormat));
        AddPlotSpecificSetting(new PlotSpecificSettingRecord(
            PlotSpecificSettingKind.AxisFontSize, "Axes", "Font size", textValue: _initial.AxisFontSize.ToString("0.##", CultureInfo.InvariantCulture)));
    }

    private enum PlotSpecificSettingKind
    {
        Label,
        ChromatogramShowPeaks,
        ChromatogramShowExpectedRtLine,
        ChromatogramShowRtLimitLines,
        ChromatogramShowSelectionCorrectedRtOnTic,
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
        TitleFontSize,
        PeakLabelFontSize,
        CalibrationPointLabelFontSize,
        PeakLabelRotate
    }

    private sealed class PlotTitleFieldEditRecord
    {
        public int SourceFieldIndex { get; set; }
        /// <summary>Per plot field only (title or labels tab layout, not the central repository).</summary>
        public bool Enabled { get; set; }
        /// <summary>Per plot field only (line-break intent before this segment).</summary>
        public bool AddLineBreakBefore { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Property { get; set; } = string.Empty;
        public string LongHeader { get; set; } = string.Empty;
        public string ShortHeader { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
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
            PlotSpecificSettingKind.ChromatogramShowExpectedRtLine or
            PlotSpecificSettingKind.ChromatogramShowRtLimitLines or
            PlotSpecificSettingKind.ChromatogramShowSelectionCorrectedRtOnTic or
            PlotSpecificSettingKind.CalibrationShowRegression or
            PlotSpecificSettingKind.CalibrationShowPointLabels or
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
        public int FieldFill { get; set; }
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
        private static readonly IGriddoOptionsCellEditor FormatEditor =
            (IGriddoOptionsCellEditor)GriddoCellEditors.StandardNumericFormatStringOptions;
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

            return r.Setting switch
            {
                PlotSpecificSettingKind.XAxisFormat or PlotSpecificSettingKind.YAxisFormat => FormatEditor.Options,
                PlotSpecificSettingKind.PeakLabelRotate => PlotPeakLabelRotation.DegreeOptionStrings.ToArray(),
                _ => EmptyOptions
            };
        }

        public bool TryGetOptionExample(object? recordSource, string option, out string example)
        {
            if (recordSource is PlotSpecificSettingRecord r
                && r.Setting == PlotSpecificSettingKind.PeakLabelRotate)
            {
                example = string.Empty;
                return false;
            }

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
    double PeakLabelFontSize,
    double CalibrationPointLabelFontSize,
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
    bool ChromatogramShowExpectedRtLine,
    bool ChromatogramShowRtLimitLines,
    bool ChromatogramShowSelectionCorrectedRtOnTic,
    bool CalibrationShowRegression,
    bool ShowCalibrationPointLabels,
    List<PlotTitleSegmentConfiguration> CalibrationPointLabelSegments,
    bool SpectrumNormalizeIntensity,
    int PeakLabelRotate);
