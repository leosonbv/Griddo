namespace Griddo.Fields;

/// <summary>
/// Optional field contract for rotating displayed header and cell text.
/// Supported values: 0, 90, 180, 270 degrees clockwise.
/// </summary>
public interface IGriddoFieldRotationView
{
    int TextRotationDegrees { get; }
}
