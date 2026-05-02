namespace Griddo;

public readonly record struct GriddoCellAddress(int RowIndex, int ColumnIndex)
{
    public bool IsValid => RowIndex >= 0 && ColumnIndex >= 0;
}
