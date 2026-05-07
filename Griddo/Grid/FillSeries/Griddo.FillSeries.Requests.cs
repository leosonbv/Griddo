namespace Griddo.Grid;

/// <summary>
/// Product notes for fill-series behavior (implemented in this partial: <see cref="Griddo.FillSelectionDown"/>,
/// <see cref="Griddo.FillSelectionIncrementalDown"/>, Ctrl+D / Ctrl+I in <c>Griddo.Input.Keyboard</c>).
/// </summary>
/// <remarks>
/// <para><b>Fill down (Ctrl+D)</b> — copies <see cref="IGriddoFieldView.GetValue"/> from the lowest selected record in each field to all higher selected records in that field. Hosted fields are skipped.</para>
/// <para><b>Incremental down (Ctrl+I)</b> — same record ordering; finds the last <c>-?\d+</c> in the formatted top cell (skipping digit runs inside <c>#RRGGBB</c>-style literals), adds 0,1,… per record, then left-pads magnitudes so replacements match both the series width and any leading zeros in the source span.</para>
/// </remarks>
internal static class GriddoFillSeriesRequests
{
}
