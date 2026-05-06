using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Griddo.Fields;
using Griddo.Editing;
using GriddoModelView;
using GriddoUi.FieldEdit;
using WpfColorFontDialog;

namespace GriddoTest;

public partial class HtmlConfigurationDialog : Window
{
    private readonly List<HtmlSegmentEditRecord> _rows = [];
    private readonly FontSummaryDialogCellEditor _fontEditor = new();
    private int _generalValueFieldIndex = -1;

    public HtmlConfigurationDialog(
        IHtmlFieldLayoutTarget seed,
        IReadOnlyList<IGriddoFieldView> allFields,
        Action<HtmlFieldConfiguration>? previewApply = null)
    {
        InitializeComponent();
        PreviewApply = previewApply;
        BuildSegmentGridFields();
        BuildGeneralGridFields();
        GeneralGrid.CellPropertyViewResolver = ResolveGeneralCellPropertyView;
        SeedFrom(seed, allFields);
        UpdateMoveButtonsVisibility();
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

    private Action<HtmlFieldConfiguration>? PreviewApply { get; }

    public HtmlFieldConfiguration? Result { get; private set; }

    private void SeedFrom(IHtmlFieldLayoutTarget seed, IReadOnlyList<IGriddoFieldView> allFields)
    {
        GeneralGrid.Records.Clear();
        GeneralGrid.Records.Add(new HtmlGeneralSettingRecord(HtmlGeneralSettingKind.CategoryField, seed.IsCategoryField));
        GeneralGrid.Records.Add(new HtmlGeneralSettingRecord(
            HtmlGeneralSettingKind.Font,
            false,
            fontFamilyName: seed.FontFamilyName,
            fontSize: seed.FontSize,
            fontStyleName: seed.FontStyleName));

        var savedByIndex = seed.Segments.ToDictionary(s => s.SourceFieldIndex);
        var excluded = new HashSet<int>();
        for (var sourceFieldIndex = 0; sourceFieldIndex < allFields.Count; sourceFieldIndex++)
        {
            var field = allFields[sourceFieldIndex];
            if (field is IHtmlFieldLayoutTarget || field is IPlotFieldLayoutTarget)
            {
                excluded.Add(sourceFieldIndex);
            }
        }

        var configuredOrder = seed.Segments
            .Select(s => s.SourceFieldIndex)
            .Where(i => i >= 0 && i < allFields.Count && !excluded.Contains(i))
            .Distinct()
            .ToList();
        var remainingOrder = Enumerable.Range(0, allFields.Count)
            .Where(i => !excluded.Contains(i) && !configuredOrder.Contains(i))
            .ToList();
        var orderedSourceIndices = configuredOrder.Concat(remainingOrder);
        SegmentsGrid.Records.Clear();
        foreach (var sourceFieldIndex in orderedSourceIndices)
        {
            var field = allFields[sourceFieldIndex];

            var sourceTitle = field is IGriddoFieldTitleView tv ? tv.AbbreviatedHeader : string.Empty;
            var saved = savedByIndex.TryGetValue(sourceFieldIndex, out var hit) ? hit : null;
            var row = new HtmlSegmentEditRecord
            {
                SourceFieldIndex = sourceFieldIndex,
                Enabled = saved?.Enabled ?? false,
                AbbreviatedHeader = saved?.AbbreviatedHeaderOverride ?? string.Empty,
                AddLineBreakAfter = saved?.AddLineBreakAfter ?? true,
                WordWrap = saved?.WordWrap ?? true,
                Header = field.Header ?? string.Empty,
                SourceAbbreviatedHeader = sourceTitle ?? string.Empty
            };
            _rows.Add(row);
            SegmentsGrid.Records.Add(row);
        }
    }

    private HtmlFieldConfiguration BuildResult()
    {
        var isCategoryField = GeneralGrid.Records
            .OfType<HtmlGeneralSettingRecord>()
            .FirstOrDefault(r => r.Setting == HtmlGeneralSettingKind.CategoryField)?.BoolValue ?? false;
        var font = GeneralGrid.Records
            .OfType<HtmlGeneralSettingRecord>()
            .FirstOrDefault(r => r.Setting == HtmlGeneralSettingKind.Font);
        return new HtmlFieldConfiguration
        {
            IsCategoryField = isCategoryField,
            FontFamilyName = font?.FontFamilyName ?? string.Empty,
            FontSize = Math.Max(0, font?.FontSize ?? 0),
            FontStyleName = font?.FontStyleName ?? string.Empty,
            Segments = SegmentsGrid.Records
                .OfType<HtmlSegmentEditRecord>()
                .Select(r => new HtmlFieldSegmentConfiguration
                {
                    SourceFieldIndex = r.SourceFieldIndex,
                    Enabled = r.Enabled,
                    AbbreviatedHeaderOverride = r.AbbreviatedHeader ?? string.Empty,
                    AddLineBreakAfter = r.AddLineBreakAfter,
                    WordWrap = r.WordWrap
                })
                .ToList()
        };
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _ = SegmentsGrid.TryMoveSelectedRecordsStep(-1);
    }

    private void MoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _ = SegmentsGrid.TryMoveSelectedRecordsStep(1);
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var result = BuildResult();
        Result = result;
        PreviewApply?.Invoke(result);
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

    private sealed class HtmlSegmentEditRecord
    {
        public int SourceFieldIndex { get; set; }
        public bool Enabled { get; set; }
        public string AbbreviatedHeader { get; set; } = string.Empty;
        public bool AddLineBreakAfter { get; set; } = true;
        public bool WordWrap { get; set; } = true;
        public string Header { get; set; } = string.Empty;
        public string SourceAbbreviatedHeader { get; set; } = string.Empty;
    }

    private void BuildSegmentGridFields()
    {
        SegmentsGrid.Fields.Clear();
        SegmentsGrid.Fields.Add(new GriddoBoolFieldView(
            "Use",
            60,
            r => ((HtmlSegmentEditRecord)r).Enabled,
            (r, v) =>
            {
                if (v is not bool b)
                {
                    return false;
                }

                ((HtmlSegmentEditRecord)r).Enabled = b;
                return true;
            }));
        SegmentsGrid.Fields.Add(new GriddoBoolFieldView(
            "Line break",
            100,
            r => ((HtmlSegmentEditRecord)r).AddLineBreakAfter,
            (r, v) =>
            {
                if (v is not bool b)
                {
                    return false;
                }

                ((HtmlSegmentEditRecord)r).AddLineBreakAfter = b;
                return true;
            }));
        SegmentsGrid.Fields.Add(new GriddoFieldView(
            "Header",
            220,
            r => ((HtmlSegmentEditRecord)r).Header,
            static (_, _) => false,
            GriddoCellEditors.Text));
        SegmentsGrid.Fields.Add(new GriddoFieldView(
            "Abbr",
            140,
            r => ((HtmlSegmentEditRecord)r).AbbreviatedHeader,
            (r, v) =>
            {
                ((HtmlSegmentEditRecord)r).AbbreviatedHeader = v?.ToString() ?? string.Empty;
                return true;
            },
            GriddoCellEditors.Text));
        SegmentsGrid.Fields.Add(new GriddoBoolFieldView(
            "Wrap",
            70,
            r => ((HtmlSegmentEditRecord)r).WordWrap,
            (r, v) =>
            {
                if (v is not bool b)
                {
                    return false;
                }

                ((HtmlSegmentEditRecord)r).WordWrap = b;
                return true;
            }));
    }

    private void BuildGeneralGridFields()
    {
        GeneralGrid.Fields.Clear();
        GeneralGrid.Fields.Add(new GriddoFieldView(
            "Setting",
            220,
            r => ((HtmlGeneralSettingRecord)r).DisplayName,
            static (_, _) => false,
            GriddoCellEditors.Text));
        _generalValueFieldIndex = GeneralGrid.Fields.Count;
        GeneralGrid.Fields.Add(new HtmlGeneralValueField(this));
    }

    private GriddoCellPropertyView? ResolveGeneralCellPropertyView(object recordSource, int fieldIndex)
    {
        if (fieldIndex != _generalValueFieldIndex || recordSource is not HtmlGeneralSettingRecord record)
        {
            return null;
        }

        if (record.Setting != HtmlGeneralSettingKind.Font)
        {
            return null;
        }

        var hasFontFamily = !string.IsNullOrWhiteSpace(record.FontFamilyName);
        var hasFontSize = record.FontSize > 0;
        var hasFontStyle = !string.IsNullOrWhiteSpace(record.FontStyleName);
        if (!hasFontFamily && !hasFontSize && !hasFontStyle)
        {
            return null;
        }

        return hasFontSize
            ? new GriddoCellPropertyView
            {
                FontFamilyName = hasFontFamily ? record.FontFamilyName : string.Empty,
                FontStyleName = hasFontStyle ? record.FontStyleName : string.Empty,
                FontSize = record.FontSize
            }
            : new GriddoCellPropertyView
            {
                FontFamilyName = hasFontFamily ? record.FontFamilyName : string.Empty,
                FontStyleName = hasFontStyle ? record.FontStyleName : string.Empty
            };
    }

    private enum HtmlGeneralSettingKind
    {
        CategoryField,
        Font
    }

    private sealed class HtmlGeneralValueField : IGriddoFieldView, IGriddoCheckboxToggleFieldView
    {
        private readonly HtmlConfigurationDialog _owner;
        private readonly FontSummaryDialogCellEditor _editor = new();

        public HtmlGeneralValueField(HtmlConfigurationDialog owner)
        {
            _owner = owner;
        }

        public string Header { get; set; } = "Value";
        public double Width => 260;
        public bool Fill { get; set; }
        public bool IsHtml => false;
        public TextAlignment ContentAlignment => TextAlignment.Left;
        public IGriddoCellEditor Editor => _editor;

        public bool IsCheckboxCell(object recordSource)
            => recordSource is HtmlGeneralSettingRecord { Setting: HtmlGeneralSettingKind.CategoryField };

        public object? GetValue(object recordSource)
        {
            if (recordSource is not HtmlGeneralSettingRecord record)
            {
                return null;
            }

            return record.GetValueDisplay();
        }

        public bool TrySetValue(object recordSource, object? value)
        {
            if (recordSource is not HtmlGeneralSettingRecord record)
            {
                return false;
            }

            if (record.Setting == HtmlGeneralSettingKind.CategoryField)
            {
                if (value is bool b)
                {
                    record.BoolValue = b;
                    return true;
                }

                return false;
            }

            var text = value?.ToString() ?? string.Empty;
            if (string.Equals(text, _editor.LaunchToken, StringComparison.Ordinal))
            {
                _owner.OpenFontDialog(record);
                return true;
            }

            return TryApplyFontSummary(record, text);
        }

        public string FormatValue(object? value) => value?.ToString() ?? string.Empty;
    }

    private sealed class HtmlGeneralSettingRecord
    {
        public HtmlGeneralSettingRecord(HtmlGeneralSettingKind setting, bool boolValue)
        {
            Setting = setting;
            BoolValue = boolValue;
        }
        public HtmlGeneralSettingRecord(
            HtmlGeneralSettingKind setting,
            bool boolValue,
            string fontFamilyName,
            double fontSize,
            string fontStyleName)
        {
            Setting = setting;
            BoolValue = boolValue;
            FontFamilyName = fontFamilyName ?? string.Empty;
            FontSize = Math.Max(0, fontSize);
            FontStyleName = fontStyleName ?? string.Empty;
        }

        public HtmlGeneralSettingKind Setting { get; }
        public bool BoolValue { get; set; }
        public string FontFamilyName { get; set; } = string.Empty;
        public double FontSize { get; set; }
        public string FontStyleName { get; set; } = string.Empty;
        public string DisplayName => Setting switch
        {
            HtmlGeneralSettingKind.CategoryField => "Category field",
            HtmlGeneralSettingKind.Font => "Font",
            _ => Setting.ToString()
        };

        public string GetValueDisplay()
        {
            if (Setting == HtmlGeneralSettingKind.CategoryField)
            {
                return BoolValue ? "True" : "False";
            }

            var family = string.IsNullOrWhiteSpace(FontFamilyName) ? "(default)" : FontFamilyName;
            var size = FontSize > 0 ? FontSize.ToString("0.#") : "default";
            var style = string.IsNullOrWhiteSpace(FontStyleName) ? "Regular" : FontStyleName;
            return $"{family}, {size}, {style}";
        }
    }

    private static bool TryApplyFontSummary(HtmlGeneralSettingRecord record, string text)
    {
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0)
        {
            record.FontFamilyName = string.Empty;
            record.FontSize = 0;
            record.FontStyleName = string.Empty;
            return true;
        }

        var parts = t.Split(',');
        if (parts.Length < 2)
        {
            return false;
        }

        record.FontFamilyName = parts[0].Trim();
        var sizeToken = parts[1].Trim();
        if (string.Equals(sizeToken, "default", StringComparison.OrdinalIgnoreCase))
        {
            record.FontSize = 0;
        }
        else if (double.TryParse(sizeToken, out var size))
        {
            record.FontSize = Math.Max(0, size);
        }
        else
        {
            return false;
        }

        record.FontStyleName = parts.Length >= 3 ? parts[2].Trim() : string.Empty;
        if (string.Equals(record.FontFamilyName, "(default)", StringComparison.OrdinalIgnoreCase))
        {
            record.FontFamilyName = string.Empty;
        }
        if (string.Equals(record.FontStyleName, "Regular", StringComparison.OrdinalIgnoreCase))
        {
            record.FontStyleName = string.Empty;
        }

        return true;
    }

    private void OpenFontDialog(HtmlGeneralSettingRecord record)
    {
        var dialog = new ColorFontDialog(
            previewFontInFontList: true,
            allowArbitraryFontSizes: true,
            showColorPicker: false)
        {
            Font = BuildDialogFontInfo(record)
        };
        if (dialog.ShowDialog() != true || dialog.Font is null)
        {
            return;
        }

        record.FontFamilyName = dialog.Font.Family?.Source ?? string.Empty;
        record.FontSize = Math.Max(0, dialog.Font.Size);
        record.FontStyleName = FormatFontStyle(dialog.Font.Style, dialog.Font.Weight);
        GeneralGrid.InvalidateVisual();
    }

    private static FontInfo BuildDialogFontInfo(HtmlGeneralSettingRecord record)
    {
        var familyName = string.IsNullOrWhiteSpace(record.FontFamilyName) ? "Segoe UI" : record.FontFamilyName;
        var family = new FontFamily(familyName);
        var (style, weight) = ParseFontTraits(record.FontStyleName);
        return new FontInfo
        {
            Family = family,
            Size = Math.Max(6, record.FontSize <= 0 ? 12 : record.FontSize),
            Style = style,
            Stretch = FontStretches.Normal,
            Weight = weight,
            BrushColor = Brushes.Black
        };
    }

    private static (FontStyle style, FontWeight weight) ParseFontTraits(string styleText)
    {
        var normalized = (styleText ?? string.Empty).ToLowerInvariant();
        var style = normalized.Contains("italic", StringComparison.Ordinal)
            ? FontStyles.Italic
            : FontStyles.Normal;
        var weight = normalized.Contains("bold", StringComparison.Ordinal)
            ? FontWeights.Bold
            : FontWeights.Normal;
        return (style, weight);
    }

    private static string FormatFontStyle(FontStyle style, FontWeight weight)
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

        return parts.Count == 0 ? string.Empty : string.Join(", ", parts);
    }
}
