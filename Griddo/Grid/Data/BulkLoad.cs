namespace Griddo.Grid;

public sealed partial class Griddo
{
    private int _bulkLoadDepth;
    private bool _deferredGridCollectionLayout;
    private bool _deferredGridCollectionSort;

    /// <summary>
    /// Defers expensive collection-change work (layout, scroll bars, sort, repaint) until <see cref="EndBulkLoad"/>.
    /// </summary>
    public void BeginBulkLoad()
    {
        _bulkLoadDepth++;
    }

    /// <summary>
    /// Flushes deferred collection-change work when the outermost bulk load ends.
    /// </summary>
    public void EndBulkLoad()
    {
        if (_bulkLoadDepth <= 0)
        {
            return;
        }

        _bulkLoadDepth--;
        if (_bulkLoadDepth > 0)
        {
            return;
        }

        if (!_deferredGridCollectionLayout && !_deferredGridCollectionSort)
        {
            return;
        }

        _deferredGridCollectionLayout = false;
        _deferredGridCollectionSort = false;
        ApplyDeferredGridCollectionUpdates();
    }

    private void ApplyImmediateGridCollectionStateUpdates()
    {
        _fixedFieldCount = Math.Clamp((int)_fixedFieldCount, 0, Math.Max((int)0, (int)Fields.Count));
        _fixedRecordCount = Math.Clamp((int)_fixedRecordCount, 0, Math.Max((int)0, (int)Records.Count));
        if (Records.Count == 0)
        {
            _hasAutoSizedFields = false;
            _initialSampleAutoSizeScheduled = false;
            _suppressInitialAutoWidthFields.Clear();
            _userFixedWidthFields.Clear();
        }
    }

    private void DeferGridCollectionLayoutAndSort()
    {
        _deferredGridCollectionLayout = true;
        if (_sortDescriptors.Count > 0 && Records.Count > 1 && Fields.Count > 0)
        {
            _deferredGridCollectionSort = true;
        }
    }

    private void ApplyDeferredGridCollectionUpdates()
    {
        if (Records.Count > 0 && Fields.Count > 0 && !_hasAutoSizedFields)
        {
            ScheduleInitialSampleAutoSize();
        }

        ReconcileVerticalScrollOffsetWithContent();
        UpdateRecordHeaderWidth();
        UpdateScrollBars();
        UpdateHostCanvasClips();
        InvalidateVisual();

        if (_sortDescriptors.Count > 0 && Records.Count > 1 && Fields.Count > 0)
        {
            ApplySorting();
        }
    }
}
