namespace GriddoModelView;

public sealed class FieldConfiguration
{
    public int SourceFieldIndex { get; set; }
    public string SourceFieldKey { get; set; } = string.Empty;
    public int FieldFill { get; set; }
    public bool Visible { get; set; }
    public double Width { get; set; }
    public int SortPriority { get; set; }
    public bool SortAscending { get; set; }
    public string Header { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public int SignificantFigures { get; set; } = 3;
    public string Format { get; set; } = string.Empty;
    public string Category { get; set; } = "General";

    /// <summary>Legacy name; prefer <see cref="SuppressCellEdit"/>. When true, treated like <see cref="SuppressCellEdit"/> for consumers that merge both.</summary>
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// When true, scalar in-place cell edit, fill-series, clipboard clear/paste, and checkbox toggles should be disabled
    /// for this column (hosted plot fields ignore this in the Griddo grid).
    /// </summary>
    public bool SuppressCellEdit { get; set; }

    public bool IsCalculated { get; set; }

    /// <summary>True when either <see cref="SuppressCellEdit"/> or legacy <see cref="IsReadOnly"/> is set.</summary>
    public bool EffectiveSuppressCellEdit => SuppressCellEdit || IsReadOnly;
}
