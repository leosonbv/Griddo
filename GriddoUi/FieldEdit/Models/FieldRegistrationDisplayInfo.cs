namespace GriddoUi.FieldEdit.Models;

/// <summary>
/// Read/write snapshot of a central field registration row (Key, headers, format reference, description).
/// Host applications (e.g. Quanto FieldRegistrationRepository) supply and persist these values.
/// </summary>
public sealed class FieldRegistrationDisplayInfo
{
    public string Key { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Property { get; init; } = string.Empty;
    public string LongHeader { get; set; } = string.Empty;
    public string ShortHeader { get; set; } = string.Empty;
    /// <summary>General format name or literal format reference (not the resolved specifier).</summary>
    public string Format { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
