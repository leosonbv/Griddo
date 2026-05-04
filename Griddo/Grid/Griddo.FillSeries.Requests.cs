namespace Griddo.Grid;

/// <summary>
/// Product notes for fill-series behavior (implemented in this partial: <see cref="Griddo.FillSelectionDown"/>,
/// <see cref="Griddo.FillSelectionIncrementalDown"/>, Ctrl+D / Ctrl+I in <c>Griddo.Input.Keyboard</c>).
/// </summary>
/// <remarks>
/// <para><b>Fill down (Ctrl+D)</b> — copies <see cref="IGriddoColumnView.GetValue"/> from the lowest selected row in each column to all higher selected rows in that column. Hosted columns are skipped.</para>
/// <para><b>Incremental down (Ctrl+I)</b> — same row ordering; finds the last <c>-?\d+</c> in the formatted top cell (skipping digit runs inside <c>#RRGGBB</c>-style literals), adds 0,1,… per row, then left-pads magnitudes so replacements match both the series width and any leading zeros in the source span.</para>
/// </remarks>
internal static class GriddoFillSeriesRequests
{
}
