using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Griddo.Fields;
using Griddo.Primitives;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    public FrameworkElement? TryGetHostedElement(GriddoCellAddress address)
    {
        return _hostedCells.GetValueOrDefault(address);
    }

    /// <summary>
    /// Clears hosted cell visuals so the next measure/render recreates and reparents them (e.g. Plotto after field chooser apply).
    /// </summary>
    public void RefreshHostedCells()
    {
        ClearHostedCells();
        InvalidateMeasure();
        InvalidateVisual();
        var grid = this;
        Dispatcher.BeginInvoke(
            () =>
            {
                grid.InvalidateMeasure();
                grid.InvalidateVisual();
            },
            DispatcherPriority.Render);
    }

    private void SyncHostedCells()
    {
        if (_viewportBodyWidth <= 0 || _viewportBodyHeight <= 0 || Records.Count == 0 || Fields.Count == 0)
        {
            ClearHostedCells();
            return;
        }

        var needed = new HashSet<GriddoCellAddress>();

        void AddHostedInFieldRange(int c0, int c1)
        {
            for (var col = c0; col <= c1; col++)
            {
                if (Fields[col] is not IGriddoHostedFieldView)
                {
                    continue;
                }

                ForEachVisibleRecord(record => needed.Add(new GriddoCellAddress(record, col)));
            }
        }

        if (IsBodyTransposed)
        {
            ForEachVisibleScrollRecordForTranspose(record =>
            {
                ForEachVisibleFieldForTranspose(col =>
                {
                    if (Fields[col] is IGriddoHostedFieldView)
                    {
                        needed.Add(new GriddoCellAddress(record, col));
                    }
                });
            });
        }
        else
        {
            if (_fixedFieldCount > 0 && Fields.Count > 0)
            {
                AddHostedInFieldRange(0, Math.Min(_fixedFieldCount, Fields.Count) - 1);
            }

            GetVisibleScrollFieldRange(out var scrollStart, out var scrollEnd, out _);
            if (scrollEnd >= scrollStart)
            {
                AddHostedInFieldRange(scrollStart, scrollEnd);
            }
        }

        if (needed.Count == 0)
        {
            ClearHostedCells();
            return;
        }

        var stale = _hostedCells.Keys.Where(k => !needed.Contains(k)).ToList();
        foreach (var key in stale)
        {
            var fe = _hostedCells[key];
            if (fe.Parent is Panel p)
            {
                p.Children.Remove(fe);
            }

            _hostedCells.Remove(key);
        }

        foreach (var addr in needed)
        {
            var field = Fields[addr.FieldIndex];
            if (field is not IGriddoHostedFieldView hostedField)
            {
                continue;
            }

            var dest = HostCanvasForField(addr.FieldIndex);
            if (!_hostedCells.TryGetValue(addr, out var host))
            {
                host = hostedField.CreateHostElement();
                _hostedCells[addr] = host;
                dest.Children.Add(host);
            }
            else if (host.Parent is Panel parent && !ReferenceEquals(parent, dest))
            {
                parent.Children.Remove(host);
                dest.Children.Add(host);
            }

            var recordData = Records[addr.RecordIndex];
            var isSelected = _selectedCells.Contains(addr);
            var isCurrent = _currentCell == addr;
            hostedField.UpdateHostElement(host, recordData, isSelected, isCurrent);
            hostedField.ApplyPlotDirectEditOption(host, HostedPlotDirectEditOnMouseDown);
            hostedField.SyncHostedUiScale(host, ContentScale);

            var rect = GetCellRect(addr.RecordIndex, addr.FieldIndex);
            if (rect.IsEmpty)
            {
                continue;
            }

            Canvas.SetLeft(host, rect.X - _recordHeaderWidth + ScaledHostedInsetX);
            Canvas.SetTop(host, rect.Y - ScaledFieldHeaderHeight + ScaledHostedInsetY);
            host.Width = Math.Max(0, rect.Width - (ScaledHostedInsetX * 2));
            host.Height = Math.Max(0, rect.Height - (ScaledHostedInsetY * 2));
        }
    }

    private Canvas HostCanvasForField(int fieldIndex) =>
        fieldIndex < _fixedFieldCount ? _fixedHostCanvas : _scrollHostCanvas;

    /// <summary>
    /// Fixed vs scroll body rectangle (below headers). Used to clip current-cell / inline edit visuals so they do not paint over the other band when a cell slides under frozen fields.
    /// </summary>
    private Rect GetFieldBodyBandClipRect(int fieldIndex)
    {
        if (IsBodyTransposed)
        {
            var fixedH = Math.Min(GetFixedFieldsWidth(), _viewportBodyHeight);
            if (fieldIndex < _fixedFieldCount)
            {
                return new Rect(_recordHeaderWidth, ScaledFieldHeaderHeight, _viewportBodyWidth, fixedH);
            }

            var scrollH = Math.Max(0, _viewportBodyHeight - fixedH);
            return new Rect(_recordHeaderWidth, ScaledFieldHeaderHeight + fixedH, _viewportBodyWidth, scrollH);
        }

        var fixedW = GetFixedFieldsWidth();
        var scrollLeft = _recordHeaderWidth + fixedW;

        if (fieldIndex < _fixedFieldCount)
        {
            var w = Math.Min(fixedW, _viewportBodyWidth);
            return new Rect(_recordHeaderWidth, ScaledFieldHeaderHeight, w, _viewportBodyHeight);
        }

        var scrollW = Math.Max(0, _viewportBodyWidth - fixedW);
        return new Rect(scrollLeft, ScaledFieldHeaderHeight, scrollW, _viewportBodyHeight);
    }

    private void UpdateHostCanvasClips()
    {
        if (IsBodyTransposed)
        {
            var bodyW = _viewportBodyWidth;
            var bodyH = _viewportBodyHeight;
            var fixedRecordsW = GetTransposeFixedRecordsWidth();
            var fixedColsH = GetFixedFieldsWidth();
            var fh = Math.Min(fixedColsH, bodyH);
            var fixedRecordsClipW = Math.Min(fixedRecordsW, bodyW);
            var scrollW = Math.Max(0, bodyW - fixedRecordsW);
            var scrollH = Math.Max(0, bodyH - fh);
            var belowFrozen = Math.Max(0, bodyH - fh);

            // Fixed-field hosts: union of top-left (frozen×frozen) and top-right (scroll records × frozen cols).
            if (fh > 1e-6)
            {
                if (fixedRecordsClipW > 1e-6 && scrollW > 1e-6)
                {
                    var gTopLeft = new RectangleGeometry(new Rect(0, 0, fixedRecordsClipW, fh));
                    var gTopRight = new RectangleGeometry(new Rect(fixedRecordsW, 0, scrollW, fh));
                    _fixedHostCanvas.Clip = new CombinedGeometry(GeometryCombineMode.Union, gTopLeft, gTopRight);
                }
                else if (fixedRecordsClipW > 1e-6)
                {
                    _fixedHostCanvas.Clip = new RectangleGeometry(new Rect(0, 0, fixedRecordsClipW, fh));
                }
                else
                {
                    _fixedHostCanvas.Clip = new RectangleGeometry(new Rect(fixedRecordsW, 0, scrollW, fh));
                }
            }
            else
            {
                _fixedHostCanvas.Clip = null;
            }

            // Scroll-field hosts: union of left strip (frozen records × scroll cols) and bottom-right (scroll×scroll).
            if (belowFrozen > 1e-6)
            {
                if (fixedRecordsClipW > 1e-6 && scrollW > 1e-6 && scrollH > 1e-6)
                {
                    var gLeft = new RectangleGeometry(new Rect(0, fh, fixedRecordsClipW, belowFrozen));
                    var gRight = new RectangleGeometry(new Rect(fixedRecordsW, fh, scrollW, scrollH));
                    _scrollHostCanvas.Clip = new CombinedGeometry(GeometryCombineMode.Union, gLeft, gRight);
                }
                else if (fixedRecordsClipW > 1e-6)
                {
                    _scrollHostCanvas.Clip = new RectangleGeometry(new Rect(0, fh, fixedRecordsClipW, belowFrozen));
                }
                else if (scrollW > 1e-6 && scrollH > 1e-6)
                {
                    _scrollHostCanvas.Clip = new RectangleGeometry(new Rect(fixedRecordsW, fh, scrollW, scrollH));
                }
                else
                {
                    _scrollHostCanvas.Clip = null;
                }
            }
            else
            {
                _scrollHostCanvas.Clip = null;
            }

            return;
        }

        var fixedW = GetFixedFieldsWidth();
        var fw = Math.Min(fixedW, _viewportBodyWidth);
        var sw = Math.Max(0, _viewportBodyWidth - fixedW);
        _fixedHostCanvas.Clip = new RectangleGeometry(new Rect(0, 0, fw, _viewportBodyHeight));
        _scrollHostCanvas.Clip = new RectangleGeometry(new Rect(fixedW, 0, sw, _viewportBodyHeight));
    }

    private void ClearHostedCells()
    {
        foreach (var kv in _hostedCells.Values.ToList())
        {
            if (kv.Parent is Panel p)
            {
                p.Children.Remove(kv);
            }
        }

        _hostedCells.Clear();
    }
}
