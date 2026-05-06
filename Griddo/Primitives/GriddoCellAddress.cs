namespace Griddo.Primitives;

public readonly record struct GriddoCellAddress(int RecordIndex, int FieldIndex)
{
    public bool IsValid => RecordIndex >= 0 && FieldIndex >= 0;
}
