namespace Griddo.Fields;

/// <summary>
/// Optional field contract for vertical merge-band visuals per record.
/// Return true when this record should visually merge with its previous/next record
/// (e.g. same group/category), so internal horizontal grid lines can be hidden.
/// </summary>
public interface IGriddoRecordMergeBandView
{
    bool IsMergedWithPreviousRecord(IReadOnlyList<object> records, int recordIndex);

    bool IsMergedWithNextRecord(IReadOnlyList<object> records, int recordIndex);
}

