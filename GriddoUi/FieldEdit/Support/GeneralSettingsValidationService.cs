using GriddoUi.FieldEdit.Models;

namespace GriddoUi.FieldEdit.Support;

public static class GeneralSettingsValidationService
{
    public static bool TryValidateFrozenFields(int value, int fieldCount, out int committed, out string? error)
    {
        committed = 0;
        if (value < 0)
        {
            error = "Frozen fields must be a non-negative integer.";
            return false;
        }

        committed = Math.Min(value, fieldCount);
        error = null;
        return true;
    }

    public static bool TryValidateFrozenRecords(int value, out int committed, out string? error)
    {
        committed = 0;
        if (value < 0)
        {
            error = "Frozen records must be a non-negative integer.";
            return false;
        }

        committed = value;
        error = null;
        return true;
    }

    public static bool TryValidateVisibleRecords(int value, out int committed, out string? error)
    {
        committed = 0;
        if (value is < 0 or > 10)
        {
            error = "Visible records must be an integer between 0 and 10.";
            return false;
        }

        committed = value;
        error = null;
        return true;
    }

    public static bool TryValidateRecordThickness(int value, bool isTransposed, out int committed, out string? error)
    {
        committed = 24;
        var minRecordThickness = (int)Math.Ceiling(global::Griddo.Grid.Griddo.GetDefaultMinimumRecordThickness());
        if (value < minRecordThickness || value > 400)
        {
            var axisLabel = isTransposed ? "width" : "height";
            error = $"Record {axisLabel} must be an integer between {minRecordThickness} and 400.";
            return false;
        }

        committed = value;
        error = null;
        return true;
    }
}
