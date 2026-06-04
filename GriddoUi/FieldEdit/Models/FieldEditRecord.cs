using Griddo.Abstractions.Fields;

namespace GriddoUi.FieldEdit.Models;

using System.Windows;
using Griddo.Fields;
using Dialog;

/// <summary>One editable record in <see cref="FieldConfigurator"/> (maps to a property on the data shape).</summary>
public sealed class FieldEditRecord
{
    /// <summary>Original field index when built from a data grid; -1 when from CLR metadata only.</summary>
    public int SourceFieldIndex { get; init; } = -1;

    /// <summary>CLR property / member name (from <see cref="IGriddoFieldSourceMember"/> or reflection inference).</summary>
    public string PropertyName { get; init; } = string.Empty;
    public string SourceObjectName { get; init; } = string.Empty;

    /// <summary>Central registration key (same as Field editor <c>Key</c> column).</summary>
    public string RegistrationKey { get; set; } = string.Empty;

    /// <summary>Long header from the central field repository (applied to grid column headers).</summary>
    public string LongHeader { get; set; } = string.Empty;

    /// <summary>Short header from the central field repository (summaries, plot titles).</summary>
    public string ShortHeader { get; set; } = string.Empty;

    /// <summary>Format reference from the central repository (general format name or literal).</summary>
    public string FormatReference { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Order/selection number. >0 means "used/checked" (checkbox shows checked); the value determines display order
    /// in field grids (always sorted by this). 0 means not used. The Use checkbox in field editor/grid settings
    /// is presentation only; toggling it sets this to 0 or next-positive.
    /// This replaces prior separate index or list position based ordering for field columns.
    /// </summary>
    public int OrderNumber { get; set; }

    /// <summary>
    /// When true, scalar in-place edit, fill-series into cells, clipboard clear/paste, and checkbox toggles are disabled for this column.
    /// Hosted plot/chart columns ignore this flag. Persisted in per-view layout JSON when the host application stores it on field rows.
    /// </summary>
    public bool SuppressCellEdit { get; set; }

    public int FieldFill { get; set; }

    public double Width { get; set; } = 140;
    public string FormatString { get; set; } = string.Empty;
    public string FontFamilyName { get; set; } = string.Empty;
    public double FontSize { get; set; }
    public string FontStyleName { get; set; } = string.Empty;
    public bool NoWrap { get; set; } = true;
    public string ForegroundColor { get; set; } = string.Empty;
    public string BackgroundColor { get; set; } = string.Empty;
    public bool IsNumericProperty { get; init; }
    public bool IsDateTimeProperty { get; init; }
    public bool IsEnumProperty { get; init; }
    public bool IsFlagsEnumProperty { get; init; }
    public int SortPriority { get; set; }
    public bool SortAscending { get; set; } = true;
    public TextAlignment ContentAlignment { get; set; } = TextAlignment.Left;

    /// <summary>Formatted sample from the first data record, if one was supplied when the dialog was built.</summary>
    public string SampleDisplay { get; init; } = string.Empty;
    public object? SampleValue { get; init; }
    public object? SampleRecordSource { get; init; }
    public IGriddoFieldView? SourceFieldView { get; init; }

    public FieldEditRecord Clone() => new()
    {
        SourceFieldIndex = SourceFieldIndex,
        PropertyName = PropertyName,
        SourceObjectName = SourceObjectName,
        RegistrationKey = RegistrationKey,
        LongHeader = LongHeader,
        ShortHeader = ShortHeader,
        FormatReference = FormatReference,
        Description = Description,
        OrderNumber = OrderNumber,
        SuppressCellEdit = SuppressCellEdit,
        FieldFill = FieldFill,
        Width = Width,
        FormatString = FormatString,
        FontFamilyName = FontFamilyName,
        FontSize = FontSize,
        FontStyleName = FontStyleName,
        NoWrap = NoWrap,
        ForegroundColor = ForegroundColor,
        BackgroundColor = BackgroundColor,
        IsNumericProperty = IsNumericProperty,
        IsDateTimeProperty = IsDateTimeProperty,
        IsEnumProperty = IsEnumProperty,
        IsFlagsEnumProperty = IsFlagsEnumProperty,
        SortPriority = SortPriority,
        SortAscending = SortAscending,
        ContentAlignment = ContentAlignment,
        SampleDisplay = SampleDisplay,
        SampleValue = SampleValue,
        SampleRecordSource = SampleRecordSource,
        SourceFieldView = SourceFieldView
    };
}
