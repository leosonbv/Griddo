using System.Globalization;
using System.Windows;
using Griddo.Fields;
using Griddo.Editing;
using GriddoUi.FieldEdit.Models;

namespace GriddoUi.FieldEdit.Support;

/// <summary>Value field for <see cref="GeneralSettingRecord"/> records: unsigned int or bool (checkbox when bool).</summary>
public sealed class GeneralSettingValueFieldView : IGriddoFieldView, IGriddoCheckboxToggleFieldView, IGriddoFieldDescriptionView
{
    public GeneralSettingValueFieldView(string header = "Value", double width = 140)
    {
        Header = header;
        Width = width;
    }

    public string Header { get; set; }
    public string Description { get; set; } = string.Empty;
    public double Width { get; }
    public int FieldFill { get; set; }
    public bool IsHtml => false;
    public TextAlignment ContentAlignment { get; } = TextAlignment.Right;
    public IGriddoCellEditor Editor => GriddoCellEditors.Number;

    public bool IsCheckboxCell(object recordSource) =>
        recordSource is GeneralSettingRecord { ValueKind: GeneralSettingValueKind.Boolean };

    public object? GetValue(object recordSource) =>
        recordSource is GeneralSettingRecord r
            ? r.ValueKind switch
            {
                GeneralSettingValueKind.Boolean => r.BoolValue,
                GeneralSettingValueKind.UnsignedInt => r.IntValue,
                _ => null
            }
            : null;

    public bool TrySetValue(object recordSource, object? value)
    {
        if (recordSource is not GeneralSettingRecord r)
        {
            return false;
        }

        if (r.ValueKind == GeneralSettingValueKind.None)
        {
            return false;
        }

        if (r.ValueKind == GeneralSettingValueKind.Boolean)
        {
            bool b;
            if (value is bool bb)
            {
                b = bb;
            }
            else if (value is double d && d is 0d or 1d)
            {
                b = d != 0d;
            }
            else if (value is string s && bool.TryParse(s.Trim(), out var pb))
            {
                b = pb;
            }
            else
            {
                return false;
            }

            r.BoolValue = b;
            return true;
        }

        int n;
        if (value is int i)
        {
            n = i;
        }
        else if (value is long l && l is >= int.MinValue and <= int.MaxValue)
        {
            n = (int)l;
        }
        else if (value is double d && d is >= int.MinValue and <= int.MaxValue && double.IsFinite(d))
        {
            n = (int)d;
        }
        else
        {
            if (!int.TryParse(value?.ToString()?.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out n))
            {
                return false;
            }
        }

        if (n < 0)
        {
            return false;
        }

        if (r.Setting == GeneralSettingKind.FillRecordsVisibleCount && n > 10)
        {
            return false;
        }

        r.IntValue = n;
        return true;
    }

    public string FormatValue(object? value) =>
        value switch
        {
            null => string.Empty,
            bool b => b.ToString(CultureInfo.CurrentCulture),
            IFormattable f => f.ToString(null, CultureInfo.CurrentCulture),
            _ => value.ToString() ?? string.Empty
        };
}
