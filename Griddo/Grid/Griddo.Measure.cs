using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    private void UpdateRowHeaderWidth()
    {
        var rowCountText = Math.Max(1, Rows.Count).ToString();
        var required = MeasureRowHeaderWidthForText(rowCountText);
        _rowHeaderWidth = Math.Max(MeasureRowHeaderWidthForText("1"), required);
    }

    private double MeasureRowHeaderWidthForText(string text)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            EffectiveFontSize,
            Brushes.Black,
            1.0);

        return Math.Ceiling(formatted.WidthIncludingTrailingWhitespace) + 14 * _contentScale;
    }

    private static double MeasureCellWidth(object? value, Typeface typeface, double fontSize)
    {
        return value switch
        {
            ImageSource image => image.Width,
            Geometry geometry => geometry.Bounds.Width,
            _ => MeasureTextWidth(value?.ToString() ?? string.Empty, typeface, fontSize)
        };
    }

    private static double MeasureCellHeight(object? value, Typeface typeface, double fontSize)
    {
        return value switch
        {
            ImageSource image => image.Height,
            Geometry geometry => geometry.Bounds.Height,
            _ => MeasureTextHeight(value?.ToString() ?? string.Empty, typeface, fontSize)
        };
    }

    private static double MeasureTextWidth(string text, Typeface typeface, double fontSize)
    {
        var formattedText = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            1.0);

        return formattedText.Width;
    }

    private static double MeasureTextHeight(string text, Typeface typeface, double fontSize)
    {
        var formattedText = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            1.0);

        return formattedText.Height;
    }
}
