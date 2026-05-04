using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Griddo.Columns;
using Griddo.Primitives;

namespace Griddo.Grid;

public sealed partial class Griddo
{
    public FrameworkElement? TryGetHostedElement(GriddoCellAddress address)
    {
        return _hostedCells.GetValueOrDefault(address);
    }

    /// <summary>
    /// Clears hosted cell visuals so the next measure/render recreates and reparents them (e.g. Plotto after column chooser apply).
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
        if (_viewportBodyWidth <= 0 || _viewportBodyHeight <= 0 || Rows.Count == 0 || Columns.Count == 0)
        {
            ClearHostedCells();
            return;
        }

        var needed = new HashSet<GriddoCellAddress>();

        void AddHostedInColumnRange(int c0, int c1)
        {
            for (var col = c0; col <= c1; col++)
            {
                if (Columns[col] is not IGriddoHostedColumnView)
                {
                    continue;
                }

                ForEachVisibleRow(row => needed.Add(new GriddoCellAddress(row, col)));
            }
        }

        if (IsBodyTransposed)
        {
            ForEachVisibleScrollRowForTranspose(row =>
            {
                ForEachVisibleColumnForTranspose(col =>
                {
                    if (Columns[col] is IGriddoHostedColumnView)
                    {
                        needed.Add(new GriddoCellAddress(row, col));
                    }
                });
            });
        }
        else
        {
            if (_fixedColumnCount > 0 && Columns.Count > 0)
            {
                AddHostedInColumnRange(0, Math.Min(_fixedColumnCount, Columns.Count) - 1);
            }

            GetVisibleScrollColumnRange(out var scrollStart, out var scrollEnd, out _);
            if (scrollEnd >= scrollStart)
            {
                AddHostedInColumnRange(scrollStart, scrollEnd);
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
            var column = Columns[addr.ColumnIndex];
            if (column is not IGriddoHostedColumnView hostedColumn)
            {
                continue;
            }

            var dest = HostCanvasForColumn(addr.ColumnIndex);
            if (!_hostedCells.TryGetValue(addr, out var host))
            {
                host = hostedColumn.CreateHostElement();
                _hostedCells[addr] = host;
                dest.Children.Add(host);
            }
            else if (host.Parent is Panel parent && !ReferenceEquals(parent, dest))
            {
                parent.Children.Remove(host);
                dest.Children.Add(host);
            }

            var rowData = Rows[addr.RowIndex];
            var isSelected = _selectedCells.Contains(addr);
            var isCurrent = _currentCell == addr;
            hostedColumn.UpdateHostElement(host, rowData, isSelected, isCurrent);
            hostedColumn.ApplyPlotDirectEditOption(host, HostedPlotDirectEditOnMouseDown);
            hostedColumn.SyncHostedUiScale(host, ContentScale);

            var rect = GetCellRect(addr.RowIndex, addr.ColumnIndex);
            if (rect.IsEmpty)
            {
                continue;
            }

            Canvas.SetLeft(host, rect.X - _rowHeaderWidth + ScaledHostedInsetX);
            Canvas.SetTop(host, rect.Y - ScaledColumnHeaderHeight + ScaledHostedInsetY);
            host.Width = Math.Max(0, rect.Width - (ScaledHostedInsetX * 2));
            host.Height = Math.Max(0, rect.Height - (ScaledHostedInsetY * 2));
        }
    }

    private Canvas HostCanvasForColumn(int columnIndex) =>
        columnIndex < _fixedColumnCount ? _fixedHostCanvas : _scrollHostCanvas;

    /// <summary>
    /// Fixed vs scroll body rectangle (below headers). Used to clip current-cell / inline edit visuals so they do not paint over the other band when a cell slides under frozen columns.
    /// </summary>
    private Rect GetColumnBodyBandClipRect(int columnIndex)
    {
        if (IsBodyTransposed)
        {
            var fixedH = Math.Min(GetFixedColumnsWidth(), _viewportBodyHeight);
            if (columnIndex < _fixedColumnCount)
            {
                return new Rect(_rowHeaderWidth, ScaledColumnHeaderHeight, _viewportBodyWidth, fixedH);
            }

            var scrollH = Math.Max(0, _viewportBodyHeight - fixedH);
            return new Rect(_rowHeaderWidth, ScaledColumnHeaderHeight + fixedH, _viewportBodyWidth, scrollH);
        }

        var fixedW = GetFixedColumnsWidth();
        var scrollLeft = _rowHeaderWidth + fixedW;

        if (columnIndex < _fixedColumnCount)
        {
            var w = Math.Min(fixedW, _viewportBodyWidth);
            return new Rect(_rowHeaderWidth, ScaledColumnHeaderHeight, w, _viewportBodyHeight);
        }

        var scrollW = Math.Max(0, _viewportBodyWidth - fixedW);
        return new Rect(scrollLeft, ScaledColumnHeaderHeight, scrollW, _viewportBodyHeight);
    }

    private void UpdateHostCanvasClips()
    {
        if (IsBodyTransposed)
        {
            var bodyW = _viewportBodyWidth;
            var bodyH = _viewportBodyHeight;
            var fixedRowsW = GetTransposeFixedRowsWidth();
            var fixedColsH = GetFixedColumnsWidth();
            var fh = Math.Min(fixedColsH, bodyH);
            var fixedRowsClipW = Math.Min(fixedRowsW, bodyW);
            var scrollW = Math.Max(0, bodyW - fixedRowsW);
            var scrollH = Math.Max(0, bodyH - fh);
            var belowFrozen = Math.Max(0, bodyH - fh);

            // Fixed-column hosts: union of top-left (frozen×frozen) and top-right (scroll rows × frozen cols).
            if (fh > 1e-6)
            {
                if (fixedRowsClipW > 1e-6 && scrollW > 1e-6)
                {
                    var gTopLeft = new RectangleGeometry(new Rect(0, 0, fixedRowsClipW, fh));
                    var gTopRight = new RectangleGeometry(new Rect(fixedRowsW, 0, scrollW, fh));
                    _fixedHostCanvas.Clip = new CombinedGeometry(GeometryCombineMode.Union, gTopLeft, gTopRight);
                }
                else if (fixedRowsClipW > 1e-6)
                {
                    _fixedHostCanvas.Clip = new RectangleGeometry(new Rect(0, 0, fixedRowsClipW, fh));
                }
                else
                {
                    _fixedHostCanvas.Clip = new RectangleGeometry(new Rect(fixedRowsW, 0, scrollW, fh));
                }
            }
            else
            {
                _fixedHostCanvas.Clip = null;
            }

            // Scroll-column hosts: union of left strip (frozen rows × scroll cols) and bottom-right (scroll×scroll).
            if (belowFrozen > 1e-6)
            {
                if (fixedRowsClipW > 1e-6 && scrollW > 1e-6 && scrollH > 1e-6)
                {
                    var gLeft = new RectangleGeometry(new Rect(0, fh, fixedRowsClipW, belowFrozen));
                    var gRight = new RectangleGeometry(new Rect(fixedRowsW, fh, scrollW, scrollH));
                    _scrollHostCanvas.Clip = new CombinedGeometry(GeometryCombineMode.Union, gLeft, gRight);
                }
                else if (fixedRowsClipW > 1e-6)
                {
                    _scrollHostCanvas.Clip = new RectangleGeometry(new Rect(0, fh, fixedRowsClipW, belowFrozen));
                }
                else if (scrollW > 1e-6 && scrollH > 1e-6)
                {
                    _scrollHostCanvas.Clip = new RectangleGeometry(new Rect(fixedRowsW, fh, scrollW, scrollH));
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

        var fixedW = GetFixedColumnsWidth();
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
