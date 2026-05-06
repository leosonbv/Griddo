using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Griddo.Fields;
using Griddo.Editing;
using Griddo.Grid;
using WpfColorFontDialog;

namespace GriddoUi.FieldEdit;

public partial class FieldConfigurator : Window
{
    private readonly List<IGriddoFieldView> _fieldHeaderRegistry = [];
    private readonly List<IGriddoFieldView> _generalFieldHeaderRegistry = [];
    private int _valueFieldIndex = -1;

    /// <summary>Fired when Apply is pressed; argument is an ordered snapshot (clones).</summary>
    public event EventHandler<IReadOnlyList<FieldEditRecord>>? PreviewApply;

    /// <summary>Grid whose fields this dialog edits (for nested "Grid configurator..." from the preview grid).</summary>
    public global::Griddo.Grid.Griddo? TargetSourceGrid { get; set; }

    /// <summary>Applies current records, frozen counts, and general options to the grid that opened the dialog.</summary>
    public Action<IReadOnlyList<FieldEditRecord>, int, int, FieldChooserGeneralOptions>? ApplyToSourceGrid { get; set; }

    /// <summary>Host hook for field header context menu (e.g. <see cref="MainWindow"/> demo menu).</summary>
    public Action<global::Griddo.Grid.Griddo, IReadOnlyList<IGriddoFieldView>, GriddoFieldHeaderMouseEventArgs>? FieldHeaderMenuHandler { get; set; }

    public IReadOnlyList<IGriddoFieldView> FieldHeaderRegistry => _fieldHeaderRegistry;
    public IReadOnlyList<IGriddoFieldView> GeneralFieldHeaderRegistry => _generalFieldHeaderRegistry;
    public global::Griddo.Grid.Griddo ConfigFieldsGrid => FieldGrid;
    public global::Griddo.Grid.Griddo ConfigGeneralSettingsGrid => GeneralPropertyGrid;

    public FieldConfigurator(
        IReadOnlyList<FieldEditRecord> templateRecords,
        int initialFrozenFields,
        int initialFrozenRecords,
        FieldChooserGeneralOptions? initialOptions = null)
    {
        InitializeComponent();
        foreach (var r in templateRecords)
        {
            FieldGrid.Records.Add(r.Clone());
        }

        BuildFields();
        var options = initialOptions ?? new FieldChooserGeneralOptions();
        BuildGeneralPropertyGrid(options, initialFrozenFields, initialFrozenRecords);
        FieldGrid.CellPropertyViewResolver = ResolveCellPropertyViewForConfigurator;
        FieldGrid.FieldHeaderRightClick += FieldGrid_FieldHeaderRightClick;
        GeneralPropertyGrid.FieldHeaderRightClick += GeneralPropertyGrid_FieldHeaderRightClick;
        Closed += (_, _) =>
        {
            FieldGrid.FieldHeaderRightClick -= FieldGrid_FieldHeaderRightClick;
            GeneralPropertyGrid.FieldHeaderRightClick -= GeneralPropertyGrid_FieldHeaderRightClick;
        };
    }

    private void BuildGeneralPropertyGrid(FieldChooserGeneralOptions options, int frozenFields, int frozenRecords)
    {
        _generalFieldHeaderRegistry.Clear();
        GeneralPropertyGrid.Fields.Clear();
        GeneralPropertyGrid.Records.Clear();
        var categoryField = new GeneralCategoryFieldView(() => GeneralPropertyGrid.Records, "Category", 130);
        var settingField = new GeneralSettingNameFieldView("Setting", 220, GetGeneralSettingDisplayName);
        var valueField = new GeneralSettingValueFieldView("Value", 120);
        GeneralPropertyGrid.Fields.Add(categoryField);
        GeneralPropertyGrid.Fields.Add(settingField);
        GeneralPropertyGrid.Fields.Add(valueField);
        _generalFieldHeaderRegistry.Add(categoryField);
        _generalFieldHeaderRegistry.Add(settingField);
        _generalFieldHeaderRegistry.Add(valueField);

        var fc = Math.Clamp(frozenFields, 0, FieldGrid.Records.Count);
        void AddGeneralSetting(
            GeneralSettingKind setting,
            GeneralSettingValueKind valueKind,
            int categoryLevel,
            string categoryKey,
            string categoryDisplay,
            string settingDisplay,
            int intValue = 0,
            bool boolValue = false)
        {
            GeneralPropertyGrid.Records.Add(
                new GeneralSettingRecord(
                    setting,
                    valueKind,
                    categoryDisplay,
                    settingDisplay,
                    categoryLevel: categoryLevel,
                    categorySortKey: categoryKey,
                    settingSortKey: setting.ToString(),
                    intValue: intValue,
                    boolValue: boolValue));
        }

        AddGeneralSetting(GeneralSettingKind.RecordThickness, GeneralSettingValueKind.UnsignedInt, 1, "Layout", "Layout", "Record thickness", intValue: options.RecordThickness);
        AddGeneralSetting(GeneralSettingKind.FillRecordsVisibleCount, GeneralSettingValueKind.UnsignedInt, 1, "Layout", "Layout", "Visible records (0-10)", intValue: options.VisibleRecordCount);
        AddGeneralSetting(GeneralSettingKind.TransposeLayout, GeneralSettingValueKind.Boolean, 1, "Layout", "Layout", "Transpose", boolValue: options.IsTransposed);
        AddGeneralSetting(GeneralSettingKind.FrozenFields, GeneralSettingValueKind.UnsignedInt, 2, "Frozen", "Frozen", "Frozen fields", intValue: fc);
        AddGeneralSetting(GeneralSettingKind.FrozenRecords, GeneralSettingValueKind.UnsignedInt, 2, "Frozen", "Frozen", "Frozen records", intValue: Math.Max(0, frozenRecords));
        AddGeneralSetting(GeneralSettingKind.ShowSelectionColor, GeneralSettingValueKind.Boolean, 3, "Selection", "Selection", "Cell selection", boolValue: options.ShowSelectionColor);
        AddGeneralSetting(GeneralSettingKind.ShowRecordSelectionColor, GeneralSettingValueKind.Boolean, 3, "Selection", "Selection", "Record headers", boolValue: options.ShowRecordSelectionColor);
        AddGeneralSetting(GeneralSettingKind.ShowColSelectionColor, GeneralSettingValueKind.Boolean, 3, "Selection", "Selection", "Field headers", boolValue: options.ShowColSelectionColor);
        AddGeneralSetting(GeneralSettingKind.ShowCurrentCellRect, GeneralSettingValueKind.Boolean, 3, "Selection", "Selection", "Current cell", boolValue: options.ShowCurrentCellRect);
        AddGeneralSetting(GeneralSettingKind.ShowEditCellRect, GeneralSettingValueKind.Boolean, 3, "Selection", "Selection", "Edit cell", boolValue: options.ShowEditCellRect);
        AddGeneralSetting(GeneralSettingKind.ImmediatePlottoEdit, GeneralSettingValueKind.Boolean, 4, "Interaction", "Interaction", "Immediate plot edit", boolValue: options.ImmediatePlottoEdit);
        AddGeneralSetting(GeneralSettingKind.ShowSortingIndicators, GeneralSettingValueKind.Boolean, 4, "Interaction", "Interaction", "Sorting indicators", boolValue: options.ShowSortingIndicators);
        AddGeneralSetting(GeneralSettingKind.ShowHorizontalScrollBar, GeneralSettingValueKind.Boolean, 5, "Scrollbars", "Scrollbars", "Horizontal scrollbar", boolValue: options.ShowHorizontalScrollBar);
        AddGeneralSetting(GeneralSettingKind.ShowVerticalScrollBar, GeneralSettingValueKind.Boolean, 5, "Scrollbars", "Scrollbars", "Vertical scrollbar", boolValue: options.ShowVerticalScrollBar);
        GeneralPropertyGrid.SetSortDescriptors(
        [
            new GriddoSortDescriptor(0, Ascending: true, Priority: 1),
            new GriddoSortDescriptor(1, Ascending: true, Priority: 2)
        ]);
    }

    private GeneralSettingRecord? GeneralRecord(GeneralSettingKind kind) =>
        GeneralPropertyGrid.Records.OfType<GeneralSettingRecord>().FirstOrDefault(r => r.Setting == kind);

    private void FieldGrid_FieldHeaderRightClick(object? sender, GriddoFieldHeaderMouseEventArgs e)
    {
        FieldHeaderMenuHandler?.Invoke(FieldGrid, _fieldHeaderRegistry, e);
    }

    private void GeneralPropertyGrid_FieldHeaderRightClick(object? sender, GriddoFieldHeaderMouseEventArgs e)
    {
        FieldHeaderMenuHandler?.Invoke(GeneralPropertyGrid, _generalFieldHeaderRegistry, e);
    }

    public IReadOnlyList<FieldEditRecord>? ResultRecords { get; private set; }

    public int ResultFrozenFields { get; private set; }

    public int ResultFrozenRecords { get; private set; }

    public FieldChooserGeneralOptions ResultGeneralOptions { get; private set; } = new();

    private void BuildFields()
    {
        FieldGrid.Fields.Clear();
        _fieldHeaderRegistry.Clear();

        void AddField(IGriddoFieldView fieldView)
        {
            FieldGrid.Fields.Add(fieldView);
            _fieldHeaderRegistry.Add(fieldView);
        }

        AddField(new GriddoBoolFieldView(
            "Vis",
            44,
            r => ((FieldEditRecord)r).Visible,
            (r, v) =>
            {
                if (v is not bool b)
                {
                    return false;
                }

                ((FieldEditRecord)r).Visible = b;
                return true;
            }));
        AddField(new GriddoBoolFieldView(
            "Fill",
            44,
            r => ((FieldEditRecord)r).Fill,
            (r, v) =>
            {
                if (v is not bool b)
                {
                    return false;
                }

                ((FieldEditRecord)r).Fill = b;
                return true;
            }));
        AddField(new GriddoFieldView(
            "Size",
            72,
            r => ((FieldEditRecord)r).Width,
            (r, v) =>
            {
                if (v is double d)
                {
                    ((FieldEditRecord)r).Width = Math.Max(28, d);
                    return true;
                }

                if (double.TryParse(v?.ToString(), out var x))
                {
                    ((FieldEditRecord)r).Width = Math.Max(28, x);
                    return true;
                }

                return false;
            },
            GriddoCellEditors.Number,
            TextAlignment.Right)
        {
            FormatString = "F0"
        });
        AddField(new GriddoFieldView(
            "Font",
            220,
            r => FormatFontSummary((FieldEditRecord)r),
            (r, v) =>
            {
                var record = (FieldEditRecord)r;
                if (string.Equals(v?.ToString(), "...", StringComparison.Ordinal))
                {
                    OpenFontEditor(ResolveTargetsForEdit(record));
                    return true;
                }

                return FontSummaryParser.TryApplyFontSummaryText(record, v?.ToString() ?? string.Empty);
            },
            new FontSummaryDialogCellEditor()));
        AddField(new GriddoFieldView(
            "Back color",
            130,
            r => FormatOneColor(((FieldEditRecord)r).BackgroundColor),
            (r, v) =>
            {
                ((FieldEditRecord)r).BackgroundColor = NormalizeBackColor(v?.ToString());
                return true;
            },
            GriddoCellEditors.KnownColorsDropdown));
        AddField(new GriddoFieldView(
            "Format",
            140,
            r => ((FieldEditRecord)r).FormatString,
            (r, v) =>
            {
                if (!TryFormatValue(v?.ToString(), out var normalized))
                {
                    return false;
                }

                ((FieldEditRecord)r).FormatString = normalized;
                return true;
            },
            GriddoCellEditors.FormatStringOptions));
        AddField(new GriddoFieldView(
            "Sort#",
            58,
            r => ((FieldEditRecord)r).SortPriority,
            (r, v) =>
            {
                if (v is double d)
                {
                    ((FieldEditRecord)r).SortPriority = Math.Max(0, (int)Math.Round(d));
                    return true;
                }

                if (int.TryParse(v?.ToString(), out var x))
                {
                    ((FieldEditRecord)r).SortPriority = Math.Max(0, x);
                    return true;
                }

                return false;
            },
            GriddoCellEditors.Number,
            TextAlignment.Right));
        AddField(new GriddoBoolFieldView(
            "Asc",
            48,
            r => ((FieldEditRecord)r).SortAscending,
            (r, v) =>
            {
                if (v is not bool b)
                {
                    return false;
                }

                ((FieldEditRecord)r).SortAscending = b;
                return true;
            }));
        AddField(new ReadonlyField("Source", 120, r => r.SourceObjectName));
        AddField(new ReadonlyField("Property", 140, r => r.PropertyName));
        AddField(new GriddoFieldView(
            "Header",
            150,
            r => ((FieldEditRecord)r).Title,
            (r, v) =>
            {
                ((FieldEditRecord)r).Title = v?.ToString() ?? string.Empty;
                return true;
            }));
        AddField(new GriddoFieldView(
            "Abbrev",
            100,
            r => ((FieldEditRecord)r).AbbreviatedTitle,
            (r, v) =>
            {
                ((FieldEditRecord)r).AbbreviatedTitle = v?.ToString() ?? string.Empty;
                return true;
            }));
        AddField(new GriddoFieldView(
            "Description",
            260,
            r => ((FieldEditRecord)r).Description,
            (r, v) =>
            {
                ((FieldEditRecord)r).Description = v?.ToString() ?? string.Empty;
                return true;
            }));
        _valueFieldIndex = FieldGrid.Fields.Count;
        AddField(new ValuePreviewField("Value", 220));
    }

    private GriddoCellPropertyView? ResolveCellPropertyViewForConfigurator(object recordSource, int fieldIndex)
    {
        if (fieldIndex != _valueFieldIndex || recordSource is not FieldEditRecord record)
        {
            return null;
        }

        return new GriddoCellPropertyView
        {
            FormatString = record.FormatString ?? string.Empty,
            FontFamilyName = record.FontFamilyName ?? string.Empty,
            FontSize = record.FontSize,
            FontStyleName = record.FontStyleName ?? string.Empty,
            ForegroundColor = record.ForegroundColor ?? string.Empty,
            BackgroundColor = record.BackgroundColor ?? string.Empty
        };
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (FieldGrid.SelectedCells.Count == 0 && FieldGrid.CurrentCell.IsValid)
        {
            FieldGrid.SelectEntireRecord(FieldGrid.CurrentCell.RecordIndex);
        }

        _ = FieldGrid.TryMoveSelectedRecordsStep(-1);
    }

    private void MoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (FieldGrid.SelectedCells.Count == 0 && FieldGrid.CurrentCell.IsValid)
        {
            FieldGrid.SelectEntireRecord(FieldGrid.CurrentCell.RecordIndex);
        }

        _ = FieldGrid.TryMoveSelectedRecordsStep(1);
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!TryCommitFrozenFields(out var frozenFields)
            || !TryCommitFrozenRecords(out var frozenRecords)
            || !TryCommitRecordThickness(out _)
            || !TryCommitVisibleRecords(out _))
        {
            return;
        }

        var records = SnapshotRecords();
        var generalOptions = BuildGeneralOptions();
        ApplyToSourceGrid?.Invoke(records, frozenFields, frozenRecords, generalOptions);
        PreviewApply?.Invoke(this, records);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!TryCommitFrozenFields(out var frozenFields)
            || !TryCommitFrozenRecords(out var frozenRecords)
            || !TryCommitRecordThickness(out _)
            || !TryCommitVisibleRecords(out _))
        {
            return;
        }

        var records = SnapshotRecords();
        var generalOptions = BuildGeneralOptions();
        ApplyToSourceGrid?.Invoke(records, frozenFields, frozenRecords, generalOptions);
        ResultRecords = records;
        ResultFrozenFields = frozenFields;
        ResultFrozenRecords = frozenRecords;
        ResultGeneralOptions = generalOptions.Clone();
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        DialogResult = false;
    }

    private bool TryCommitFrozenFields(out int frozenFields)
    {
        frozenFields = 0;
        var record = GeneralRecord(GeneralSettingKind.FrozenFields);
        if (record is null)
        {
            return false;
        }

        var fc = record.IntValue;
        if (fc < 0)
        {
            System.Windows.MessageBox.Show(
                this,
                "Frozen fields must be a non-negative integer.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        frozenFields = Math.Min(fc, FieldGrid.Records.Count);
        return true;
    }

    private bool TryCommitFrozenRecords(out int frozenRecords)
    {
        frozenRecords = 0;
        var record = GeneralRecord(GeneralSettingKind.FrozenRecords);
        if (record is null)
        {
            return false;
        }

        var fr = record.IntValue;
        if (fr < 0)
        {
            System.Windows.MessageBox.Show(
                this,
                "Frozen records must be a non-negative integer.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        frozenRecords = fr;
        return true;
    }

    private bool TryCommitVisibleRecords(out int visibleRecords)
    {
        visibleRecords = 0;
        var record = GeneralRecord(GeneralSettingKind.FillRecordsVisibleCount);
        if (record is null)
        {
            return false;
        }

        var vr = record.IntValue;
        if (vr < 0 || vr > 10)
        {
            System.Windows.MessageBox.Show(
                this,
                "Visible records must be an integer between 0 and 10.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        visibleRecords = vr;
        return true;
    }

    private bool TryCommitRecordThickness(out int recordThickness)
    {
        recordThickness = 24;
        var record = GeneralRecord(GeneralSettingKind.RecordThickness);
        if (record is null)
        {
            return false;
        }

        var rh = record.IntValue;
        var minRecordThickness = (int)Math.Ceiling(global::Griddo.Grid.Griddo.GetDefaultMinimumRecordThickness());
        if (rh < minRecordThickness || rh > 400)
        {
            var axisLabel = IsGeneralLayoutTransposed() ? "width" : "height";
            System.Windows.MessageBox.Show(
                this,
                $"Record {axisLabel} must be an integer between {minRecordThickness} and 400.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        recordThickness = rh;
        return true;
    }

    private FieldChooserGeneralOptions BuildGeneralOptions()
    {
        _ = TryCommitRecordThickness(out var recordThickness);
        _ = TryCommitVisibleRecords(out var visibleRecords);
        return new FieldChooserGeneralOptions
        {
            RecordThickness = recordThickness,
            VisibleRecordCount = visibleRecords,
            ShowSelectionColor = GeneralRecord(GeneralSettingKind.ShowSelectionColor)?.BoolValue ?? true,
            ShowCurrentCellRect = GeneralRecord(GeneralSettingKind.ShowCurrentCellRect)?.BoolValue ?? true,
            ShowRecordSelectionColor = GeneralRecord(GeneralSettingKind.ShowRecordSelectionColor)?.BoolValue ?? true,
            ShowColSelectionColor = GeneralRecord(GeneralSettingKind.ShowColSelectionColor)?.BoolValue ?? true,
            ShowEditCellRect = GeneralRecord(GeneralSettingKind.ShowEditCellRect)?.BoolValue ?? true,
            ShowSortingIndicators = GeneralRecord(GeneralSettingKind.ShowSortingIndicators)?.BoolValue ?? true,
            ShowHorizontalScrollBar = GeneralRecord(GeneralSettingKind.ShowHorizontalScrollBar)?.BoolValue ?? true,
            ShowVerticalScrollBar = GeneralRecord(GeneralSettingKind.ShowVerticalScrollBar)?.BoolValue ?? true,
            IsTransposed = GeneralRecord(GeneralSettingKind.TransposeLayout)?.BoolValue ?? false,
            ImmediatePlottoEdit = GeneralRecord(GeneralSettingKind.ImmediatePlottoEdit)?.BoolValue ?? false
        };
    }

    private List<FieldEditRecord> SnapshotRecords() =>
        FieldGrid.Records.Cast<FieldEditRecord>().Select(static r => r.Clone()).ToList();

    private List<FieldEditRecord> GetSelectedEditorRecords()
    {
        var selectedIndices = FieldGrid.SelectedCells
            .Select(c => c.RecordIndex)
            .Where(i => i >= 0 && i < FieldGrid.Records.Count)
            .Distinct()
            .OrderBy(i => i)
            .ToList();

        if (selectedIndices.Count == 0 && FieldGrid.CurrentCell.IsValid)
        {
            selectedIndices.Add(FieldGrid.CurrentCell.RecordIndex);
        }

        return selectedIndices
            .Where(i => i >= 0 && i < FieldGrid.Records.Count)
            .Select(i => FieldGrid.Records[i] as FieldEditRecord)
            .Where(r => r is not null)
            .Cast<FieldEditRecord>()
            .ToList();
    }

    private List<FieldEditRecord> ResolveTargetsForEdit(FieldEditRecord record)
    {
        var selected = GetSelectedEditorRecords();
        if (selected.Any(r => ReferenceEquals(r, record)))
        {
            return selected;
        }

        return [record];
    }

    private void OpenFontEditor(IReadOnlyList<FieldEditRecord> targets)
    {
        if (targets.Count == 0)
        {
            return;
        }

        var seed = targets[0];
        var dialog = new ColorFontDialog(
            previewFontInFontList: true,
            allowArbitraryFontSizes: true,
            showColorPicker: true)
        {
            Font = BuildDialogFontInfo(seed, seed.ForegroundColor)
        };

        if (dialog.ShowDialog() != true || dialog.Font is null)
        {
            return;
        }

        var style = FormatFontStyle(dialog.Font.Style, dialog.Font.Weight, HasUnderlineStyle(seed.FontStyleName));
        foreach (var record in targets)
        {
            record.FontFamilyName = dialog.Font.Family?.Source ?? record.FontFamilyName;
            record.FontSize = Math.Max(6, dialog.Font.Size);
            record.FontStyleName = style;
            if (dialog.Font.BrushColor is SolidColorBrush solidBrush)
            {
                record.ForegroundColor = ToHexColor(solidBrush);
            }
        }

        FieldGrid.InvalidateVisual();
    }

    private static FontInfo BuildDialogFontInfo(FieldEditRecord record, string colorText)
    {
        var familyName = string.IsNullOrWhiteSpace(record.FontFamilyName) ? "Segoe UI" : record.FontFamilyName;
        var family = new FontFamily(familyName);
        var (style, weight, _) = ParseFontTraits(record.FontStyleName);
        return new FontInfo
        {
            Family = family,
            Size = Math.Max(6, record.FontSize <= 0 ? 12 : record.FontSize),
            Style = style,
            Stretch = FontStretches.Normal,
            Weight = weight,
            BrushColor = ParseSolidColorBrush(colorText)
        };
    }

    private static SolidColorBrush ParseSolidColorBrush(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Brushes.Black;
        }

        try
        {
            if (new BrushConverter().ConvertFromString(text) is SolidColorBrush solid)
            {
                return solid;
            }

            return Brushes.Black;
        }
        catch
        {
            return Brushes.Black;
        }
    }

    private static string FormatFontSummary(FieldEditRecord record)
    {
        var family = string.IsNullOrWhiteSpace(record.FontFamilyName) ? "(default)" : record.FontFamilyName;
        var size = record.FontSize > 0 ? record.FontSize.ToString("0.#") : "default";
        var style = string.IsNullOrWhiteSpace(record.FontStyleName) ? "Regular" : record.FontStyleName;
        var fg = string.IsNullOrWhiteSpace(record.ForegroundColor) ? "(default)" : record.ForegroundColor;
        return $"{family}, {size}, {style}, Fg:{fg}";
    }

    private static string FormatOneColor(string color)
    {
        return string.IsNullOrWhiteSpace(color) ? "(default)" : color;
    }

    private static string NormalizeBackColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "(default)", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return value.Trim();
    }

    private static bool TryFormatValue(string? value, out string normalized)
    {
        normalized = string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "(none)", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : value.Trim();

        if (normalized.Length == 0)
        {
            return true;
        }

        var numberValid = true;
        try
        {
            _ = 12345.6789.ToString(normalized, System.Globalization.CultureInfo.CurrentCulture);
        }
        catch (FormatException)
        {
            numberValid = false;
        }

        var dateValid = true;
        try
        {
            _ = DateTime.Now.ToString(normalized, System.Globalization.CultureInfo.CurrentCulture);
        }
        catch (FormatException)
        {
            dateValid = false;
        }

        return numberValid || dateValid;
    }

    private static bool HasUnderlineStyle(string style)
    {
        var normalized = (style ?? string.Empty).ToLowerInvariant();
        return normalized.Contains("underline", StringComparison.Ordinal);
    }

    private static (FontStyle style, FontWeight weight, bool underline) ParseFontTraits(string styleText)
    {
        var normalized = (styleText ?? string.Empty).ToLowerInvariant();
        var style = normalized.Contains("italic", StringComparison.Ordinal)
            ? FontStyles.Italic
            : FontStyles.Normal;
        var weight = normalized.Contains("bold", StringComparison.Ordinal)
            ? FontWeights.Bold
            : FontWeights.Normal;
        var underline = normalized.Contains("underline", StringComparison.Ordinal);
        return (style, weight, underline);
    }

    private static string FormatFontStyle(FontStyle style, FontWeight weight, bool underline)
    {
        var parts = new List<string>();
        if (style == FontStyles.Italic)
        {
            parts.Add("Italic");
        }

        if (weight == FontWeights.Bold)
        {
            parts.Add("Bold");
        }

        if (underline)
        {
            parts.Add("Underline");
        }

        return parts.Count == 0 ? string.Empty : string.Join(", ", parts);
    }

    private static string ToHexColor(SolidColorBrush brush)
    {
        var c = brush.Color;
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private sealed class ReadonlyField : IGriddoFieldView, IGriddoFieldDescriptionView
    {
        private readonly Func<FieldEditRecord, object?> _get;

        public ReadonlyField(string header, double width, Func<FieldEditRecord, object?> get)
        {
            Header = header;
            Width = width;
            _get = get;
        }

        public string Header { get; set; }
        public string Description { get; set; } = string.Empty;
        public double Width { get; }
        public bool Fill { get; set; }
        public bool IsHtml => false;
        public TextAlignment ContentAlignment { get; } = TextAlignment.Left;
        public IGriddoCellEditor Editor => GriddoCellEditors.Text;

        public object? GetValue(object recordSource) => _get((FieldEditRecord)recordSource);

        public bool TrySetValue(object recordSource, object? value) => true;

        public string FormatValue(object? value) => value?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Shows category text once per contiguous category block, on the middle record,
    /// so it appears vertically centered like a merged cell.
    /// </summary>
    private sealed class GeneralCategoryFieldView : IGriddoFieldView, IGriddoFieldDescriptionView, IGriddoRecordMergeBandView, IGriddoFieldSortValueView
    {
        private readonly Func<System.Collections.IList> _recordsAccessor;

        public GeneralCategoryFieldView(Func<System.Collections.IList> recordsAccessor, string header, double width)
        {
            _recordsAccessor = recordsAccessor;
            Header = header;
            Width = width;
        }

        public string Header { get; set; }
        public string Description { get; set; } = string.Empty;
        public double Width { get; }
        public bool Fill { get; set; }
        public bool IsHtml => false;
        public TextAlignment ContentAlignment { get; } = TextAlignment.Left;
        public IGriddoCellEditor Editor => GriddoCellEditors.Text;

        public object? GetValue(object recordSource)
        {
            if (recordSource is not GeneralSettingRecord record)
            {
                return string.Empty;
            }

            return record.Category;
        }

        public bool TrySetValue(object recordSource, object? value)
        {
            _ = recordSource;
            _ = value;
            return false;
        }

        public string FormatValue(object? value) => value?.ToString() ?? string.Empty;

        public object? GetSortValue(object recordSource)
        {
            return recordSource is GeneralSettingRecord record
                ? $"{record.CategoryLevel:D3}:{record.CategorySortKey}"
                : null;
        }

        public bool IsMergedWithPreviousRecord(IReadOnlyList<object> records, int recordIndex)
        {
            if (recordIndex <= 0 || recordIndex >= records.Count)
            {
                return false;
            }

            return records[recordIndex] is GeneralSettingRecord current
                && records[recordIndex - 1] is GeneralSettingRecord prev
                && string.Equals(current.Category, prev.Category, StringComparison.Ordinal);
        }

        public bool IsMergedWithNextRecord(IReadOnlyList<object> records, int recordIndex)
        {
            if (recordIndex < 0 || recordIndex >= records.Count - 1)
            {
                return false;
            }

            return records[recordIndex] is GeneralSettingRecord current
                && records[recordIndex + 1] is GeneralSettingRecord next
                && string.Equals(current.Category, next.Category, StringComparison.Ordinal);
        }
    }

    private sealed class GeneralSettingNameFieldView(
        string header,
        double width,
        Func<GeneralSettingRecord, string> displaySelector) : IGriddoFieldView, IGriddoFieldDescriptionView, IGriddoFieldSortValueView
    {
        public string Header { get; set; } = header;
        public string Description { get; set; } = string.Empty;
        public double Width { get; } = width;
        public bool Fill { get; set; }
        public bool IsHtml => false;
        public TextAlignment ContentAlignment { get; } = TextAlignment.Left;
        public IGriddoCellEditor Editor => GriddoCellEditors.Text;

        public object? GetValue(object recordSource) =>
            recordSource is GeneralSettingRecord record ? displaySelector(record) : string.Empty;

        public bool TrySetValue(object recordSource, object? value)
        {
            _ = recordSource;
            _ = value;
            return false;
        }

        public string FormatValue(object? value) => value?.ToString() ?? string.Empty;

        public object? GetSortValue(object recordSource) =>
            recordSource is GeneralSettingRecord record ? record.SettingSortKey : null;
    }

    private bool IsGeneralLayoutTransposed() =>
        GeneralRecord(GeneralSettingKind.TransposeLayout)?.BoolValue ?? false;

    private string GetGeneralSettingDisplayName(GeneralSettingRecord record)
    {
        if (record.Setting != GeneralSettingKind.RecordThickness)
        {
            return record.DisplayName;
        }

        return IsGeneralLayoutTransposed() ? "Record width" : "Record height";
    }

    private sealed class ValuePreviewField(string header, double width) : IGriddoFieldView, IGriddoHostedFieldView, IGriddoFieldDescriptionView
    {
        public string Header { get; set; } = header;
        public string Description { get; set; } = string.Empty;
        public double Width { get; } = width;
        public bool Fill { get; set; }
        public bool IsHtml => false;
        public TextAlignment ContentAlignment { get; } = TextAlignment.Left;
        public IGriddoCellEditor Editor => GriddoCellEditors.Text;

        public object? GetValue(object recordSource) => recordSource is FieldEditRecord record ? record.SampleValue ?? record.SampleDisplay : string.Empty;
        public bool TrySetValue(object recordSource, object? value) => true;
        public string FormatValue(object? value) => value?.ToString() ?? string.Empty;

        public FrameworkElement CreateHostElement() => new ContentControl();

        public void UpdateHostElement(FrameworkElement host, object recordSource, bool isSelected, bool isCurrentCell)
        {
            if (host is not ContentControl content || recordSource is not FieldEditRecord record)
            {
                return;
            }

            if (record.SourceFieldView is IGriddoHostedFieldView hostedSource && record.SampleRecordSource is not null)
            {
                var active = content.Content as FrameworkElement;
                if (active is null || !ReferenceEquals(content.Tag, hostedSource))
                {
                    active = hostedSource.CreateHostElement();
                    content.Content = active;
                    content.Tag = hostedSource;
                }

                hostedSource.UpdateHostElement(active, record.SampleRecordSource, isSelected, isCurrentCell);
                return;
            }

            // Match main grid painting (HTML, geometry, images) — TextBlock only showed literal markup / type names.
            var needsPaintedPreview = content.Content is not PaintedValueSamplePresenter
                || content.Tag is IGriddoHostedFieldView;
            if (needsPaintedPreview)
            {
                content.Content = new PaintedValueSamplePresenter();
                content.Tag = null;
            }

            content.Background = null;
            ((PaintedValueSamplePresenter)content.Content).UpdateRecord(record);
        }

        public bool IsHostInEditMode(FrameworkElement host)
        {
            _ = host;
            return false;
        }

        public void SetHostEditMode(FrameworkElement host, bool isEditing)
        {
            _ = host;
            _ = isEditing;
        }

        public bool TryGetClipboardHtmlFragment(FrameworkElement? host, object recordSource, int cellWidthPx, int cellHeightPx, out string htmlFragment)
        {
            _ = host;
            _ = recordSource;
            _ = cellWidthPx;
            _ = cellHeightPx;
            htmlFragment = string.Empty;
            return false;
        }

        public void SyncHostedUiScale(FrameworkElement host, double contentScale)
        {
            _ = host;
            _ = contentScale;
        }

        /// <summary>Renders the sample like <see cref="GriddoValuePainter"/> (HTML, Path geometry, images).</summary>
        private sealed class PaintedValueSamplePresenter : FrameworkElement
        {
            private FieldEditRecord? _record;

            public void UpdateRecord(FieldEditRecord record)
            {
                _record = record;
                InvalidateVisual();
            }

            protected override void OnRender(DrawingContext dc)
            {
                if (_record is null || ActualWidth <= 0 || ActualHeight <= 0)
                {
                    return;
                }

                var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
                if (TryBrush(_record.BackgroundColor) is { } bg)
                {
                    dc.DrawRectangle(bg, null, bounds);
                }

                object? rawValue = _record.SampleValue ?? _record.SampleDisplay;
                var sourceCol = _record.SourceFieldView;
                var fieldIsHtml = sourceCol?.IsHtml ?? false;

                object? checkboxRecordContext = _record.SampleRecordSource ?? _record;
                if (sourceCol is IGriddoCheckboxToggleFieldView toggleCol
                    && toggleCol.IsCheckboxCell(checkboxRecordContext))
                {
                    object? boolRaw = _record.SampleRecordSource is not null
                        ? sourceCol.GetValue(_record.SampleRecordSource)
                        : rawValue;
                    var isChecked = boolRaw is true;
                    GriddoValuePainter.DrawBoolCheckbox(dc, isChecked, bounds, fontSize: _record.FontSize > 0 ? _record.FontSize : 12.0);
                    return;
                }

                var isGraphicOrSizedImage = rawValue is ImageSource or Geometry or IGriddoSizedImageValue;
                object? paintValue = fieldIsHtml || isGraphicOrSizedImage
                    ? rawValue
                    : FormatSample(_record);

                var typeface = ResolveTypeface(_record);
                var fontSize = _record.FontSize > 0 ? _record.FontSize : 12.0;
                var fg = TryBrush(_record.ForegroundColor) ?? Brushes.Black;
                var normalizedStyle = NormalizeStyle(_record.FontStyleName);
                var underline = normalizedStyle.Contains("underline", StringComparison.Ordinal);

                var alignment = sourceCol?.ContentAlignment ?? TextAlignment.Left;
                var vert = isGraphicOrSizedImage ? VerticalAlignment.Top : VerticalAlignment.Center;

                GriddoValuePainter.Paint(
                    dc,
                    paintValue,
                    bounds,
                    typeface,
                    fontSize,
                    fg,
                    underline,
                    treatAsHtml: fieldIsHtml,
                    autoDetectHtml: !fieldIsHtml,
                    alignment,
                    vert);
            }

            private static Typeface ResolveTypeface(FieldEditRecord record)
            {
                var familyName = string.IsNullOrWhiteSpace(record.FontFamilyName) ? "Segoe UI" : record.FontFamilyName.Trim();
                var normalizedStyle = NormalizeStyle(record.FontStyleName);
                var style = normalizedStyle.Contains("italic", StringComparison.Ordinal) ? FontStyles.Italic : FontStyles.Normal;
                var weight = normalizedStyle.Contains("bold", StringComparison.Ordinal) ? FontWeights.Bold : FontWeights.Normal;
                try
                {
                    return new Typeface(new FontFamily(familyName), style, weight, FontStretches.Normal);
                }
                catch
                {
                    return new Typeface(new FontFamily("Segoe UI"), style, weight, FontStretches.Normal);
                }
            }
        }

        private static Brush? TryBrush(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                return new BrushConverter().ConvertFromString(text) as Brush;
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeStyle(string style)
        {
            if (string.IsNullOrWhiteSpace(style))
            {
                return string.Empty;
            }

            return style
                .Trim()
                .Replace("-", " ", StringComparison.Ordinal)
                .Replace("_", " ", StringComparison.Ordinal)
                .ToLowerInvariant();
        }

        private static string FormatSample(FieldEditRecord record)
        {
            if (record.SampleValue is null)
            {
                return record.SampleDisplay;
            }

            if (!string.IsNullOrWhiteSpace(record.FormatString) && record.SampleValue is IFormattable formattable)
            {
                try
                {
                    return formattable.ToString(record.FormatString, CultureInfo.CurrentCulture);
                }
                catch (FormatException)
                {
                    // Fall through.
                }
            }

            return record.SampleValue switch
            {
                string text => text,
                IFormattable fmt => fmt.ToString(null, CultureInfo.CurrentCulture),
                _ => record.SampleValue.ToString() ?? string.Empty
            };
        }
    }

}
