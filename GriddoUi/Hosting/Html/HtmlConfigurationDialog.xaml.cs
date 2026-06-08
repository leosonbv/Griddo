using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
using GriddoUi.FieldEdit.Support;
using WpfColorFontDialog;

namespace GriddoUi.Hosting.Html;

public partial class HtmlConfigurationDialog : Window
{
    private readonly List<HtmlSegmentEditRecord> _rows = [];
    private readonly FontSummaryCellEditor _fontEditor = new();
    private int _generalValueFieldIndex = -1;
    private readonly IReadOnlyList<IGriddoFieldView> _allFields;
    private readonly Func<IGriddoFieldView, FieldRegistrationDisplayInfo?>? _resolveRegistration;
    private readonly Action<FieldRegistrationDisplayInfo>? _persistRegistration;

    public global::Griddo.Grid.Griddo HtmlGeneralGrid => GeneralGrid;
    public global::Griddo.Grid.Griddo HtmlSegmentsGrid => SegmentsGrid;

    public HtmlConfigurationDialog(
        IHtmlFieldLayoutTarget seed,
        IReadOnlyList<IGriddoFieldView> allFields,
        Action<HtmlFieldConfiguration>? previewApply = null,
        Func<IGriddoFieldView, FieldRegistrationDisplayInfo?>? resolveRegistration = null,
        Action<FieldRegistrationDisplayInfo>? persistRegistration = null)
    {
        InitializeComponent();
        _allFields = allFields;
        _resolveRegistration = resolveRegistration;
        _persistRegistration = persistRegistration;
        PreviewApply = previewApply;
        BuildSegmentGridFields();
        BuildGeneralGridFields();
        GeneralGrid.CellPropertyViewResolver = ResolveGeneralCellPropertyView;
        SeedFrom(seed, allFields);
        UpdateMoveButtonsVisibility();
        Loaded += OnHtmlDialogLoadedOnce;
    }

    private void OnHtmlDialogLoadedOnce(object sender, RoutedEventArgs e)
    {
        Loaded -= OnHtmlDialogLoadedOnce;
        Dispatcher.BeginInvoke(new Action(SelectEnabledSegmentRowsAndScrollFirst), DispatcherPriority.Loaded);
    }

    /// <summary>Select every segment row with Use=true; scroll the first enabled row to the viewport center.</summary>
    private void SelectEnabledSegmentRowsAndScrollFirst()
    {
        var indices = new List<int>();
        for (var i = 0; i < SegmentsGrid.Records.Count; i++)
        {
            if (SegmentsGrid.Records[i] is HtmlSegmentEditRecord { Enabled: true })
            {
                indices.Add(i);
            }
        }

        if (indices.Count == 0)
        {
            return;
        }

        SegmentsGrid.ClearCellSelection();
        for (var i = 0; i < indices.Count; i++)
        {
            SegmentsGrid.SelectEntireRecord(indices[i], additive: i > 0);
        }

        SegmentsGrid.CenterCellInViewport(new GriddoCellAddress(indices[0], 0));
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        UpdateMoveButtonsVisibility();
    }

    private void UpdateMoveButtonsVisibility()
    {
        // Move buttons only make sense for the Fields tab (reordering segments).
        // Settings tab is now first (to look more like the other dialog).
        var show = MainTabs.SelectedIndex == 1; // 0=Settings, 1=Fields
        var visibility = show ? Visibility.Visible : Visibility.Collapsed;
        MoveUpButton.Visibility = visibility;
        MoveDownButton.Visibility = visibility;
    }

    private Action<HtmlFieldConfiguration>? PreviewApply { get; }

    public HtmlFieldConfiguration? Result { get; private set; }

    private void SeedFrom(IHtmlFieldLayoutTarget seed, IReadOnlyList<IGriddoFieldView> allFields)
    {
        GeneralGrid.Records.Clear();
        GeneralGrid.Records.Add(new HtmlGeneralSettingRecord(HtmlGeneralSettingKind.Table, seed.IsTable));
        GeneralGrid.Records.Add(new HtmlGeneralSettingRecord(HtmlGeneralSettingKind.CategoryField, seed.IsCategoryField));
        GeneralGrid.Records.Add(new HtmlGeneralSettingRecord(
            HtmlGeneralSettingKind.Font,
            false,
            fontFamilyName: seed.FontFamilyName,
            fontSize: seed.FontSize,
            fontStyleName: seed.FontStyleName));

        var savedByIndex = seed.Segments
            .Select(s => (segment: s, resolved: ResolveEffectiveSourceFieldIndex(s, allFields)))
            .Where(x => x.resolved >= 0)
            .GroupBy(x => x.resolved)
            .ToDictionary(g => g.Key, g => g.First().segment);
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
            .Select(s => ResolveEffectiveSourceFieldIndex(s, allFields))
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
            var saved = savedByIndex.TryGetValue(sourceFieldIndex, out var hitByIndex) ? hitByIndex : null;
            var row = CreateSegmentEditRecord(field, sourceFieldIndex, saved);
            _rows.Add(row);
            SegmentsGrid.Records.Add(row);
        }
    }

    private HtmlFieldConfiguration BuildResult()
    {
        var isTable = GeneralGrid.Records
            .OfType<HtmlGeneralSettingRecord>()
            .FirstOrDefault(r => r.Setting == HtmlGeneralSettingKind.Table)?.BoolValue ?? true;
        var isCategoryField = GeneralGrid.Records
            .OfType<HtmlGeneralSettingRecord>()
            .FirstOrDefault(r => r.Setting == HtmlGeneralSettingKind.CategoryField)?.BoolValue ?? false;
        var font = GeneralGrid.Records
            .OfType<HtmlGeneralSettingRecord>()
            .FirstOrDefault(r => r.Setting == HtmlGeneralSettingKind.Font);
        return new HtmlFieldConfiguration
        {
            IsTable = isTable,
            IsCategoryField = isCategoryField,
            FontFamilyName = font?.FontFamilyName ?? string.Empty,
            FontSize = Math.Max(0, font?.FontSize ?? 0),
            FontStyleName = font?.FontStyleName ?? string.Empty,
            Segments = SegmentsGrid.Records
                .OfType<HtmlSegmentEditRecord>()
                .Select(r =>
                {
                    PersistRegistrationFromRow(r);
                    return new HtmlFieldSegmentConfiguration
                    {
                        SourceObjectName = r.Source.Trim(),
                        PropertyName = r.Property.Trim(),
                        SourceFieldIndex = r.SourceFieldIndex,
                        Enabled = r.Enabled,
                        AddLineBreakBefore = r.AddLineBreakBefore,
                        WordWrap = true
                    };
                })
                .ToList()
        };
    }

    private void PersistRegistrationFromRow(HtmlSegmentEditRecord row)
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
        SegmentsGrid.CommitPendingCellEdit();
        foreach (var row in SegmentsGrid.Records.OfType<HtmlSegmentEditRecord>())
        {
            PersistRegistrationFromRow(row);
        }
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
        PersistAllRegistrationRows();
        var result = BuildResult();
        Result = result;
        PreviewApply?.Invoke(result);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        PersistAllRegistrationRows();
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
        /// <summary>Per composed HTML field only (persisted in view layout, not the central repository).</summary>
        public bool Enabled { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Property { get; set; } = string.Empty;
        public string LongHeader { get; set; } = string.Empty;
        public string ShortHeader { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool AddLineBreakBefore { get; set; }
    }

    private static int ResolveEffectiveSourceFieldIndex(
        HtmlFieldSegmentConfiguration segment,
        IReadOnlyList<IGriddoFieldView> allFields)
    {
        return HostingSegmentFieldResolver.Resolve(
            allFields,
            segment.SourceObjectName,
            segment.PropertyName,
            segment.SourceFieldIndex);
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
            })
        {
            Description = "Use: include this segment in this composed HTML field only (saved per grid view, not in the central field repository)"
        });
        SegmentsGrid.Fields.Add(new GriddoFieldView(
            "Key",
            220,
            r => ((HtmlSegmentEditRecord)r).Key,
            static (_, _) => false,
            GriddoCellEditors.Text)
        {
            Description = "Unique registration key (Source.Property), same as the Field editor"
        });
        SegmentsGrid.Fields.Add(new GriddoFieldView(
            "Source",
            160,
            r => ((HtmlSegmentEditRecord)r).Source,
            static (_, _) => false,
            GriddoCellEditors.Text)
        {
            Description = "Source object name from the central field registration"
        });
        SegmentsGrid.Fields.Add(new GriddoFieldView(
            "Property",
            120,
            r => ((HtmlSegmentEditRecord)r).Property,
            static (_, _) => false,
            GriddoCellEditors.Text)
        {
            Description = "Property name from the central field registration"
        });
        SegmentsGrid.Fields.Add(new GriddoFieldView(
            "Long Header",
            140,
            r => ((HtmlSegmentEditRecord)r).LongHeader,
            (r, v) =>
            {
                var row = (HtmlSegmentEditRecord)r;
                row.LongHeader = v?.ToString() ?? string.Empty;
                PersistRegistrationFromRow(row);
                return true;
            },
            GriddoCellEditors.Text)
        {
            Description = "Long header for data grid column headers (central repository)"
        });
        SegmentsGrid.Fields.Add(new GriddoFieldView(
            "Short Header",
            100,
            r => ((HtmlSegmentEditRecord)r).ShortHeader,
            (r, v) =>
            {
                var row = (HtmlSegmentEditRecord)r;
                row.ShortHeader = v?.ToString() ?? string.Empty;
                PersistRegistrationFromRow(row);
                return true;
            },
            GriddoCellEditors.Text)
        {
            Description = "Short header for HTML segment labels, plot titles, and summaries (central repository)"
        });
        SegmentsGrid.Fields.Add(new GriddoFieldView(
            "Format",
            80,
            r => ((HtmlSegmentEditRecord)r).Format,
            (r, v) =>
            {
                var row = (HtmlSegmentEditRecord)r;
                row.Format = v?.ToString() ?? string.Empty;
                PersistRegistrationFromRow(row);
                return true;
            },
            GriddoCellEditors.FormatStringOptions)
        {
            Description = "Format reference: general format name or literal (central repository)"
        });
        SegmentsGrid.Fields.Add(new GriddoFieldView(
            "Description",
            260,
            r => ((HtmlSegmentEditRecord)r).Description,
            (r, v) =>
            {
                var row = (HtmlSegmentEditRecord)r;
                row.Description = v?.ToString() ?? string.Empty;
                PersistRegistrationFromRow(row);
                return true;
            },
            GriddoCellEditors.Text)
        {
            Description = "Field description / tooltip (central repository)"
        });
    }

    private HtmlSegmentEditRecord CreateSegmentEditRecord(
        IGriddoFieldView field,
        int sourceFieldIndex,
        HtmlFieldSegmentConfiguration? saved)
    {
        var reg = _resolveRegistration?.Invoke(field);
        var row = new HtmlSegmentEditRecord
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

    private void BuildGeneralGridFields()
    {
        GeneralGrid.Fields.Clear();
        GeneralGrid.Fields.Add(new GriddoFieldView(
            "Setting",
            220,
            r => ((HtmlGeneralSettingRecord)r).DisplayName,
            static (_, _) => false,
            GriddoCellEditors.Text)
        {
            Description = "General cell display setting name"
        });
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
        Table,
        CategoryField,
        Font
    }

    private sealed class HtmlGeneralValueField : IGriddoFieldView, IGriddoCheckboxToggleFieldView
    {
        private readonly HtmlConfigurationDialog _owner;
        private readonly FontSummaryCellEditor _editor = new();

        public HtmlGeneralValueField(HtmlConfigurationDialog owner)
        {
            _owner = owner;
        }

        public string Header { get; set; } = "Value";
        public double Width => 260;
        public int FieldFill { get; set; }
        public bool IsHtml => false;
        public TextAlignment ContentAlignment => TextAlignment.Left;
        public IGriddoCellEditor Editor => _editor;

        public bool IsCheckboxCell(object recordSource)
            => recordSource is HtmlGeneralSettingRecord { Setting: HtmlGeneralSettingKind.CategoryField or HtmlGeneralSettingKind.Table };

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

            if (record.Setting == HtmlGeneralSettingKind.CategoryField || record.Setting == HtmlGeneralSettingKind.Table)
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
            HtmlGeneralSettingKind.Table => "Table",
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
