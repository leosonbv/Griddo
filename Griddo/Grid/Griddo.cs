using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Griddo;

public sealed class Griddo : FrameworkElement
{
    private const double ColumnHeaderHeightBase = 22;
    private const double DefaultRowHeight = 24;
    private const double MinColumnWidth = 28;
    private const double MinRowHeight = 18;
    private const double ResizeGripSize = 4;
    private const double ScrollBarSize = 14;
    private const double CurrentCellBorderThickness = 1.0;
    private const double HostedCellInsetX = 2.0;
    private const double HostedCellInsetY = 1.5;

    /// <summary>
    /// Ctrl+wheel moves between these scale factors (100% = 1.0). Sorted ascending; bounds match <see cref="ContentScale"/> clamp.
    /// </summary>
    private static ReadOnlySpan<double> ContentScaleStops =>
    [
        0.25, 0.5, 0.75, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0, 4.0
    ];

    private readonly HashSet<GriddoCellAddress> _selectedCells = [];
    private readonly HashSet<GriddoCellAddress> _selectionDragSnapshot = [];
    private readonly Dictionary<int, double> _columnWidthOverrides = [];
    private readonly GriddoTextEditSession _editSession = new();
    private readonly VisualCollection _children;
    private readonly Canvas _scrollHostCanvas = new()
    {
        ClipToBounds = true,
        SnapsToDevicePixels = true,
        IsHitTestVisible = true,
        Focusable = false
    };
    private readonly Canvas _fixedHostCanvas = new()
    {
        ClipToBounds = true,
        SnapsToDevicePixels = true,
        IsHitTestVisible = true,
        Focusable = false
    };
    private readonly Dictionary<GriddoCellAddress, FrameworkElement> _hostedCells = [];
    private readonly ScrollBar _horizontalScrollBar;
    private readonly ScrollBar _verticalScrollBar;
    private readonly Grid _scaleFeedbackLayer = new()
    {
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed
    };
    private readonly TextBlock _scaleFeedbackText = new()
    {
        Foreground = Brushes.White,
        FontSize = 18,
        FontWeight = FontWeights.SemiBold
    };
    private readonly DispatcherTimer _scaleFeedbackTimer;
    private double _uniformRowHeight = DefaultRowHeight;
    private GriddoCellAddress _currentCell = new(0, 0);
    private bool _isEditing;
    private bool _hasKeyboardSelectionAnchor;
    private GriddoCellAddress _keyboardSelectionAnchor;
    private bool _isDraggingSelection;
    private bool _dragIsAdditive;
    private GriddoCellAddress _dragAnchorCell;
    private GriddoCellAddress _dragCurrentCell;
    private bool _isDraggingRowHeaderSelection;
    private bool _rowHeaderDragIsAdditive;
    private int _rowHeaderDragAnchorRow = -1;
    private int _rowHeaderDragCurrentRow = -1;
    private bool _isDraggingColumnHeaderSelection;
    private bool _columnHeaderDragIsAdditive;
    private int _columnHeaderDragAnchorColumn = -1;
    private int _columnHeaderDragCurrentColumn = -1;
    private bool _pendingHostedEditActivation;
    private GriddoCellAddress _pendingHostedEditCell;
    private bool _isResizingColumn;
    private bool _isResizingRow;
    private int _resizingColumnIndex = -1;
    private int _resizingRowIndex = -1;
    private Point _resizeStartPoint;
    private double _resizeInitialSize;
    private bool _isTrackingColumnMove;
    private bool _isMovingColumn;
    private bool _isMovingPointerInColumnHeader;
    private int _movingColumnIndex = -1;
    private int _columnMoveCueIndex = -1;
    private Point _columnMoveStartPoint;
    private bool _columnMoveStartedFromSelectedHeader;
    private bool _pendingColumnHeaderSelectionOnMouseUp;
    private int _pendingColumnHeaderIndex = -1;
    private bool _pendingColumnHeaderSelectionAdditive;
    private bool _isTrackingRowMove;
    private bool _isMovingRow;
    private int _movingRowIndex = -1;
    private int _rowMoveCueIndex = -1;
    private Point _rowMoveStartPoint;
    private bool _pendingRowHeaderSelectionOnMouseUp;
    private int _pendingRowHeaderIndex = -1;
    private bool _pendingRowHeaderSelectionAdditive;
    private double _horizontalOffset;
    private int _fixedColumnCount;
    private double _verticalOffset;
    private double _viewportBodyWidth;
    private double _viewportBodyHeight;
    private double _rowHeaderWidth = 40;

    public Griddo()
    {
        Focusable = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Rows = new ObservableCollection<object>();
        Columns = new ObservableCollection<IGriddoColumnView>();
        _children = new VisualCollection(this);

        _horizontalScrollBar = new ScrollBar
        {
            Orientation = Orientation.Horizontal,
            Minimum = 0
        };
        _horizontalScrollBar.ValueChanged += OnHorizontalScrollChanged;

        _children.Add(_scrollHostCanvas);

        _verticalScrollBar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Minimum = 0
        };
        _verticalScrollBar.ValueChanged += OnVerticalScrollChanged;

        _children.Add(_horizontalScrollBar);
        _children.Add(_verticalScrollBar);

        var scaleBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 45, 45, 48)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 10, 16, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = _scaleFeedbackText
        };
        _scaleFeedbackLayer.Children.Add(scaleBadge);

        _scaleFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _scaleFeedbackTimer.Tick += OnScaleFeedbackTimerTick;

        _children.Add(_fixedHostCanvas);

        _children.Add(_scaleFeedbackLayer);

        Rows.CollectionChanged += OnGridCollectionChanged;
        Columns.CollectionChanged += OnGridCollectionChanged;
        UpdateRowHeaderWidth();
    }

    public ObservableCollection<object> Rows { get; }
    public ObservableCollection<IGriddoColumnView> Columns { get; }

    public IReadOnlyCollection<GriddoCellAddress> SelectedCells => _selectedCells;

    public event EventHandler<GriddoColumnHeaderMouseEventArgs>? ColumnHeaderRightClick;

    /// <summary>Optional context menu for body-cell right-click (after selection rules are applied).</summary>
    public ContextMenu? CellContextMenu { get; set; }

    /// <summary>Fires before <see cref="CellContextMenu"/> opens; set <see cref="GriddoCellContextMenuEventArgs.Handled"/> to suppress the default menu.</summary>
    public event EventHandler<GriddoCellContextMenuEventArgs>? CellContextMenuOpening;

    /// <summary>Uniform row height for all rows (minimum applies).</summary>
    public double UniformRowHeight
    {
        get => _uniformRowHeight;
        set
        {
            _uniformRowHeight = Math.Max(MinRowHeight, value);
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public Brush GridLineBrush { get; set; } = Brushes.LightGray;
    public Brush HeaderBackground { get; set; } = new SolidColorBrush(Color.FromRgb(245, 245, 245));
    public Brush SelectionBackground { get; set; } = new SolidColorBrush(Color.FromArgb(120, 102, 178, 255));
    public Brush CurrentCellBorderBrush { get; set; } = Brushes.DodgerBlue;

    /// <summary>Pen stroke for the right edge of the last fixed column only (freeze boundary before scrollable columns).</summary>
    public Brush FixedColumnRightBorderBrush { get; set; } = new SolidColorBrush(Color.FromRgb(118, 118, 118));

    /// <summary>Number of leading columns that remain fixed on the left when scrolling horizontally (0 = off).</summary>
    public int FixedColumnCount
    {
        get => _fixedColumnCount;
        set
        {
            var v = Math.Clamp(value, 0, Math.Max(0, Columns.Count));
            if (v == _fixedColumnCount)
            {
                return;
            }

            _fixedColumnCount = v;
            UpdateScrollBars();
            UpdateHostCanvasClips();
            InvalidateVisual();
        }
    }

    private double _contentScale = 1.0;
    private bool _hostedPlotDirectEditOnMouseDown;

    /// <summary>
    /// When true, a single left click on a hosted Plot column activates chart edit mode on mouse down and forwards that press to the chart.
    /// </summary>
    public bool HostedPlotDirectEditOnMouseDown
    {
        get => _hostedPlotDirectEditOnMouseDown;
        set
        {
            if (_hostedPlotDirectEditOnMouseDown == value)
            {
                return;
            }

            _hostedPlotDirectEditOnMouseDown = value;
            InvalidateVisual();
        }
    }

    /// <summary>Ctrl+mouse wheel: scales row/column sizes, cell fonts, grid lines, and hosted Plotto stroke widths.</summary>
    public double ContentScale
    {
        get => _contentScale;
        set
        {
            var v = Math.Clamp(value, 0.25, 4.0);
            if (Math.Abs(v - _contentScale) < 1e-9)
            {
                return;
            }

            _contentScale = v;
            UpdateRowHeaderWidth();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private void ShowScaleFeedback()
    {
        var pct = Math.Round(ContentScale * 100.0);
        _scaleFeedbackText.Text = string.Create(CultureInfo.InvariantCulture, $"{pct}%");
        _scaleFeedbackLayer.Visibility = Visibility.Visible;
        _scaleFeedbackTimer.Stop();
        _scaleFeedbackTimer.Start();
    }

    private void OnScaleFeedbackTimerTick(object? sender, EventArgs e)
    {
        _scaleFeedbackTimer.Stop();
        _scaleFeedbackLayer.Visibility = Visibility.Collapsed;
    }

    private double GetFixedColumnsWidth()
    {
        var n = Math.Clamp(_fixedColumnCount, 0, Columns.Count);
        var w = 0.0;
        for (var i = 0; i < n; i++)
        {
            w += GetColumnWidth(i);
        }

        return w;
    }

    private double GetScrollViewportWidth() => Math.Max(0, _viewportBodyWidth - GetFixedColumnsWidth());

    private double GetScrollableContentWidth()
    {
        var total = 0.0;
        for (var col = _fixedColumnCount; col < Columns.Count; col++)
        {
            total += GetColumnWidth(col);
        }

        return total;
    }

    /// <summary>Maps a point in the column area to horizontal content X (0 = left edge of column 0).</summary>
    private bool TryMapViewportPointToContentX(double pointX, out double contentX)
    {
        if (pointX < _rowHeaderWidth || pointX > _rowHeaderWidth + _viewportBodyWidth)
        {
            contentX = 0;
            return false;
        }

        var rel = pointX - _rowHeaderWidth;
        var fixedW = GetFixedColumnsWidth();
        if (rel < fixedW)
        {
            contentX = rel;
        }
        else
        {
            contentX = fixedW + (rel - fixedW) + _horizontalOffset;
        }

        return true;
    }

    private void GetVisibleScrollColumnRange(out int startCol, out int endCol, out double startX)
    {
        startCol = _fixedColumnCount;
        endCol = _fixedColumnCount - 1;
        startX = _rowHeaderWidth + GetFixedColumnsWidth();

        if (Columns.Count == 0 || _viewportBodyWidth <= 0 || _fixedColumnCount >= Columns.Count)
        {
            return;
        }

        var scrollVp = GetScrollViewportWidth();
        if (scrollVp <= 0)
        {
            return;
        }

        var contentLeft = _horizontalOffset;
        var contentRight = _horizontalOffset + scrollVp;

        var x = 0.0;
        var col = _fixedColumnCount;
        while (col < Columns.Count)
        {
            var width = GetColumnWidth(col);
            if (x + width > contentLeft)
            {
                break;
            }

            x += width;
            col++;
        }

        if (col >= Columns.Count)
        {
            startCol = Columns.Count - 1;
            endCol = Columns.Count - 1;
            startX = _rowHeaderWidth + GetFixedColumnsWidth() + x - _horizontalOffset;
            return;
        }

        startCol = col;
        startX = _rowHeaderWidth + GetFixedColumnsWidth() + x - _horizontalOffset;
        endCol = startCol;
        var cursor = x;
        while (endCol < Columns.Count)
        {
            cursor += GetColumnWidth(endCol);
            if (cursor >= contentRight)
            {
                break;
            }

            endCol++;
        }

        endCol = Math.Clamp(endCol, startCol, Columns.Count - 1);
    }

    private static double StepContentScaleStop(double current, bool zoomIn)
    {
        const double eps = 1e-9;
        var stops = ContentScaleStops;
        if (zoomIn)
        {
            foreach (var s in stops)
            {
                if (s > current + eps)
                {
                    return s;
                }
            }

            return stops[stops.Length - 1];
        }

        for (var i = stops.Length - 1; i >= 0; i--)
        {
            var s = stops[i];
            if (s < current - eps)
            {
                return s;
            }
        }

        return stops[0];
    }

    private double ScaledColumnHeaderHeight => ColumnHeaderHeightBase * _contentScale;

    private double EffectiveFontSize => 12.0 * _contentScale;

    private double GridPenThickness => Math.Max(0.5, 1.0 * _contentScale);

    private double ScaledResizeGrip => ResizeGripSize * _contentScale;

    private double ScaledHostedInsetX => HostedCellInsetX * _contentScale;

    private double ScaledHostedInsetY => HostedCellInsetY * _contentScale;

    private double ScaledCurrentCellBorder => CurrentCellBorderThickness * _contentScale;

    protected override int VisualChildrenCount => _children.Count;

    protected override Visual GetVisualChild(int index) => _children[index];

    protected override Size MeasureOverride(Size availableSize)
    {
        const double outerBorderInset = 1;
        _horizontalScrollBar.Measure(availableSize);
        _verticalScrollBar.Measure(availableSize);
        var bodyW = Math.Max(0, availableSize.Width - _rowHeaderWidth - ScrollBarSize - outerBorderInset);
        var bodyH = Math.Max(0, availableSize.Height - ScaledColumnHeaderHeight - ScrollBarSize - outerBorderInset);
        var bodySize = new Size(bodyW, bodyH);
        _scrollHostCanvas.Measure(bodySize);
        _fixedHostCanvas.Measure(bodySize);
        _scaleFeedbackLayer.Measure(availableSize);
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        const double outerBorderInset = 1;
        var arrangedScrollBarThickness = Math.Max(0, ScrollBarSize - outerBorderInset);
        _viewportBodyWidth = Math.Max(0, finalSize.Width - _rowHeaderWidth - ScrollBarSize - outerBorderInset);
        _viewportBodyHeight = Math.Max(0, finalSize.Height - ScaledColumnHeaderHeight - ScrollBarSize - outerBorderInset);

        _horizontalScrollBar.Arrange(new Rect(
            _rowHeaderWidth,
            Math.Max(0, finalSize.Height - ScrollBarSize - outerBorderInset),
            _viewportBodyWidth,
            arrangedScrollBarThickness));

        _verticalScrollBar.Arrange(new Rect(
            Math.Max(0, finalSize.Width - ScrollBarSize - outerBorderInset),
            ScaledColumnHeaderHeight,
            arrangedScrollBarThickness,
            _viewportBodyHeight));

        UpdateScrollBars();

        UpdateHostCanvasClips();

        var bodyRect = new Rect(_rowHeaderWidth, ScaledColumnHeaderHeight, _viewportBodyWidth, _viewportBodyHeight);
        _scrollHostCanvas.Arrange(bodyRect);
        _fixedHostCanvas.Arrange(bodyRect);

        _scaleFeedbackLayer.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }

    public FrameworkElement? TryGetHostedElement(GriddoCellAddress address)
    {
        return _hostedCells.TryGetValue(address, out var fe) ? fe : null;
    }

    private void SyncHostedCells()
    {
        if (_viewportBodyWidth <= 0 || _viewportBodyHeight <= 0 || Rows.Count == 0 || Columns.Count == 0)
        {
            ClearHostedCells();
            return;
        }

        var needed = new HashSet<GriddoCellAddress>();
        GetVisibleRowRange(out var startRow, out var endRow);
        if (endRow < startRow)
        {
            ClearHostedCells();
            return;
        }

        void AddHostedInColumnRange(int c0, int c1)
        {
            for (var col = c0; col <= c1; col++)
            {
                if (Columns[col] is not IGriddoHostedColumnView)
                {
                    continue;
                }

                for (var row = startRow; row <= endRow; row++)
                {
                    needed.Add(new GriddoCellAddress(row, col));
                }
            }
        }

        if (_fixedColumnCount > 0 && Columns.Count > 0)
        {
            AddHostedInColumnRange(0, Math.Min(_fixedColumnCount, Columns.Count) - 1);
        }

        GetVisibleScrollColumnRange(out var scrollStart, out var scrollEnd, out _);
        if (scrollEnd >= scrollStart)
        {
            AddHostedInColumnRange(scrollStart, scrollEnd);
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

    protected override void OnRender(DrawingContext dc)
    {
        SyncHostedCells();
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));
        DrawHeaders(dc);
        DrawBody(dc);
        DrawEditingText(dc);
        DrawCurrentCellOverlay(dc);
        DrawRowMoveCue(dc);
        DrawScrollBarCorner(dc);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        Keyboard.Focus(this);
        _pendingHostedEditActivation = false;
        _hasKeyboardSelectionAnchor = false;
        var pointer = e.GetPosition(this);
        var modifiers = Keyboard.Modifiers;
        var isCtrlPressed = (modifiers & ModifierKeys.Control) != 0;
        var isShiftPressed = (modifiers & ModifierKeys.Shift) != 0;
        var oldCurrentCell = _currentCell;

        if (e.ChangedButton == MouseButton.Right && HitTestColumnHeader(pointer) is var rightHeader && rightHeader >= 0)
        {
            ColumnHeaderRightClick?.Invoke(this, new GriddoColumnHeaderMouseEventArgs(rightHeader));
            e.Handled = true;
            base.OnMouseDown(e);
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            var dividerColumn = HitTestColumnDivider(pointer);
            if (dividerColumn >= 0)
            {
                if (e.ClickCount == 2)
                {
                    AutoSizeColumn(dividerColumn);
                    e.Handled = true;
                    return;
                }

                _isResizingColumn = true;
                _resizingColumnIndex = dividerColumn;
                _resizeStartPoint = pointer;
                _resizeInitialSize = GetColumnWidth(dividerColumn);
                CaptureMouse();
                e.Handled = true;
                return;
            }

            var dividerRow = HitTestRowDivider(pointer);
            if (dividerRow >= 0)
            {
                if (e.ClickCount == 2)
                {
                    AutoSizeRow(dividerRow);
                    e.Handled = true;
                    return;
                }

                _isResizingRow = true;
                _resizingRowIndex = dividerRow;
                _resizeStartPoint = pointer;
                _resizeInitialSize = GetRowHeight(dividerRow);
                CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        if (HitTestTopLeftHeaderCell(pointer))
        {
            SelectAllCells();
            _isEditing = false;
            InvalidateVisual();
            e.Handled = true;
            base.OnMouseDown(e);
            return;
        }

        var clickedColumnHeader = HitTestColumnHeader(pointer);
        if (clickedColumnHeader >= 0)
        {
            var target = new GriddoCellAddress(
                Rows.Count == 0 ? 0 : Math.Clamp(oldCurrentCell.RowIndex, 0, Rows.Count - 1),
                clickedColumnHeader);
            var clickedSelectedColumnHeader = IsColumnSelected(clickedColumnHeader);

            if (e.ChangedButton == MouseButton.Left
                && clickedSelectedColumnHeader
                && !isShiftPressed
                && Rows.Count > 0
                && Columns.Count > 0)
            {
                _isTrackingColumnMove = true;
                _isMovingColumn = false;
                _columnMoveStartedFromSelectedHeader = true;
                _movingColumnIndex = clickedColumnHeader;
                _columnMoveCueIndex = -1;
                _columnMoveStartPoint = pointer;
                _pendingColumnHeaderSelectionOnMouseUp = true;
                _pendingColumnHeaderIndex = clickedColumnHeader;
                _pendingColumnHeaderSelectionAdditive = isCtrlPressed;
                CaptureMouse();
                e.Handled = true;
                base.OnMouseDown(e);
                return;
            }

            if (isShiftPressed && oldCurrentCell.IsValid && Rows.Count > 0 && Columns.Count > 0)
            {
                SelectRange(oldCurrentCell, target, isCtrlPressed);
                IncludeColumnsRangeForSelectedRowsOnColumn(oldCurrentCell.ColumnIndex, clickedColumnHeader);
            }
            else
            {
                SelectColumn(clickedColumnHeader, isCtrlPressed);
            }

            _currentCell = target;
            _isEditing = false;
            InvalidateVisual();
            if (!isShiftPressed
                && e.ChangedButton == MouseButton.Left
                && Rows.Count > 0
                && Columns.Count > 0)
            {
                _columnHeaderDragIsAdditive = isCtrlPressed;
                _selectionDragSnapshot.Clear();
                _selectionDragSnapshot.UnionWith(_selectedCells);
                _columnHeaderDragAnchorColumn = clickedColumnHeader;
                _columnHeaderDragCurrentColumn = clickedColumnHeader;
                _isDraggingColumnHeaderSelection = true;
                CaptureMouse();
            }

            e.Handled = true;
            base.OnMouseDown(e);
            return;
        }

        var clickedRowHeader = HitTestRowHeader(pointer);
        if (clickedRowHeader >= 0)
        {
            var target = new GriddoCellAddress(
                clickedRowHeader,
                Columns.Count == 0 ? 0 : Math.Clamp(oldCurrentCell.ColumnIndex, 0, Columns.Count - 1));
            var clickedSelectedRowHeader = IsRowSelected(clickedRowHeader);

            if (e.ChangedButton == MouseButton.Left
                && clickedSelectedRowHeader
                && !isShiftPressed
                && Rows.Count > 0
                && Columns.Count > 0)
            {
                _isTrackingRowMove = true;
                _isMovingRow = false;
                _movingRowIndex = clickedRowHeader;
                _rowMoveCueIndex = clickedRowHeader;
                _rowMoveStartPoint = pointer;
                _pendingRowHeaderSelectionOnMouseUp = true;
                _pendingRowHeaderIndex = clickedRowHeader;
                _pendingRowHeaderSelectionAdditive = isCtrlPressed;
                CaptureMouse();
                e.Handled = true;
                base.OnMouseDown(e);
                return;
            }

            if (isShiftPressed && oldCurrentCell.IsValid && Rows.Count > 0 && Columns.Count > 0)
            {
                SelectRange(oldCurrentCell, target, isCtrlPressed);
                IncludeRowsRangeForSelectedColumnsOnRow(oldCurrentCell.RowIndex, clickedRowHeader);
            }
            else
            {
                SelectRow(clickedRowHeader, isCtrlPressed);
            }

            if (!isShiftPressed
                && e.ChangedButton == MouseButton.Left
                && Rows.Count > 0
                && Columns.Count > 0)
            {
                _rowHeaderDragIsAdditive = isCtrlPressed;
                _selectionDragSnapshot.Clear();
                _selectionDragSnapshot.UnionWith(_selectedCells);
                _rowHeaderDragAnchorRow = clickedRowHeader;
                _rowHeaderDragCurrentRow = clickedRowHeader;
                _isDraggingRowHeaderSelection = true;
                CaptureMouse();
            }

            _currentCell = target;
            _isEditing = false;
            InvalidateVisual();
            e.Handled = true;
            base.OnMouseDown(e);
            return;
        }

        var clicked = HitTestCell(pointer);
        if (!clicked.IsValid)
        {
            base.OnMouseDown(e);
            return;
        }

        if (e.ChangedButton == MouseButton.Right
            && HostedPlotDirectEditOnMouseDown
            && e.ClickCount == 1
            && Columns[clicked.ColumnIndex] is IGriddoHostedColumnView hostedRightDirect)
        {
            _selectedCells.Clear();
            _selectedCells.Add(clicked);
            _currentCell = clicked;
            _isDraggingSelection = false;
            _pendingHostedEditActivation = false;
            SyncHostedCells();
            SetCurrentHostedCellEditMode(true);
            if (TryGetHostedElement(clicked) is FrameworkElement hostForRightRelay)
            {
                hostedRightDirect.RelayDirectEditMouseDown(hostForRightRelay, e);
            }

            _isEditing = false;
            InvalidateVisual();
            e.Handled = true;
            base.OnMouseDown(e);
            return;
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            var wasSelected = _selectedCells.Contains(clicked);
            if (!wasSelected)
            {
                _selectedCells.Clear();
                _selectedCells.Add(clicked);
                _currentCell = clicked;
            }

            _isEditing = false;
            InvalidateVisual();
            OpenCellContextMenu(e, clicked, wasSelected);
            e.Handled = true;
            base.OnMouseDown(e);
            return;
        }

        if (e.ChangedButton == MouseButton.Left
            && !isShiftPressed
            && !isCtrlPressed)
        {
            if (e.ClickCount == 2)
            {
                _selectedCells.Clear();
                _selectedCells.Add(clicked);
                _currentCell = clicked;
                _isDraggingSelection = false;
                if (Columns[clicked.ColumnIndex] is not IGriddoHostedColumnView)
                {
                    BeginEditWithoutReplacing();
                }

                InvalidateVisual();
                e.Handled = true;
                base.OnMouseDown(e);
                return;
            }

            if (HostedPlotDirectEditOnMouseDown
                && e.ClickCount == 1
                && Columns[clicked.ColumnIndex] is IGriddoHostedColumnView hostedDirect)
            {
                if (TryGetHostedElement(clicked) is FrameworkElement hostedForDirect
                    && hostedDirect.IsHostInEditMode(hostedForDirect))
                {
                    InvalidateVisual();
                    base.OnMouseDown(e);
                    return;
                }

                _selectedCells.Clear();
                _selectedCells.Add(clicked);
                _currentCell = clicked;
                _isDraggingSelection = false;
                _pendingHostedEditActivation = false;
                SyncHostedCells();
                SetCurrentHostedCellEditMode(true);
                if (TryGetHostedElement(clicked) is FrameworkElement hostForRelay)
                {
                    hostedDirect.RelayDirectEditMouseDown(hostForRelay, e);
                }

                InvalidateVisual();
                e.Handled = true;
                base.OnMouseDown(e);
                return;
            }

            if (e.ClickCount == 1
                && oldCurrentCell.IsValid
                && clicked == oldCurrentCell)
            {
                if (Columns[clicked.ColumnIndex] is IGriddoHostedColumnView hostedSameCell)
                {
                    if (TryGetHostedElement(clicked) is FrameworkElement hostedSameElement
                        && hostedSameCell.IsHostInEditMode(hostedSameElement))
                    {
                        InvalidateVisual();
                        base.OnMouseDown(e);
                        return;
                    }

                    _pendingHostedEditActivation = true;
                    _pendingHostedEditCell = clicked;
                }
                else
                {
                    _selectedCells.Clear();
                    _selectedCells.Add(clicked);
                    _currentCell = clicked;
                    _isDraggingSelection = false;
                    BeginEditWithoutReplacing();
                    InvalidateVisual();
                    e.Handled = true;
                    base.OnMouseDown(e);
                    return;
                }
            }
        }

        if (e.ChangedButton == MouseButton.Left
            && isShiftPressed
            && oldCurrentCell.IsValid
            && Rows.Count > 0
            && Columns.Count > 0)
        {
            SelectRange(oldCurrentCell, clicked, isCtrlPressed);
            _currentCell = clicked;
            _isEditing = false;
            InvalidateVisual();
            e.Handled = true;
            base.OnMouseDown(e);
            return;
        }

        if (e.ChangedButton == MouseButton.Left
            && Columns[clicked.ColumnIndex] is IGriddoHostedColumnView hostedForEdit
            && TryGetHostedElement(clicked) is FrameworkElement hostedElement
            && hostedForEdit.IsHostInEditMode(hostedElement))
        {
            if (isCtrlPressed)
            {
                _selectedCells.Add(clicked);
            }
            else
            {
                _selectedCells.Clear();
                _selectedCells.Add(clicked);
            }

            _currentCell = clicked;
            _isDraggingSelection = false;
            _isEditing = false;
            InvalidateVisual();
            base.OnMouseDown(e);
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            base.OnMouseDown(e);
            return;
        }

        _dragIsAdditive = isCtrlPressed;

        if (_dragIsAdditive)
        {
            _selectedCells.Add(clicked);
        }
        else
        {
            _selectedCells.Clear();
            _selectedCells.Add(clicked);
        }

        _selectionDragSnapshot.Clear();
        _selectionDragSnapshot.UnionWith(_selectedCells);
        _dragAnchorCell = clicked;
        _dragCurrentCell = clicked;
        _isDraggingSelection = true;
        CaptureMouse();

        _currentCell = clicked;
        _isEditing = false;
        InvalidateVisual();
        e.Handled = true;
        base.OnMouseDown(e);
    }

    private void OpenCellContextMenu(MouseButtonEventArgs e, GriddoCellAddress cell, bool cellWasAlreadySelected)
    {
        var pos = e.GetPosition(this);
        var args = new GriddoCellContextMenuEventArgs(cell, pos, cellWasAlreadySelected);
        CellContextMenuOpening?.Invoke(this, args);
        if (args.Handled || CellContextMenu is null)
        {
            return;
        }

        CellContextMenu.PlacementTarget = this;
        CellContextMenu.Placement = PlacementMode.RelativePoint;
        CellContextMenu.HorizontalOffset = pos.X;
        CellContextMenu.VerticalOffset = pos.Y;
        CellContextMenu.IsOpen = true;
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            base.OnPreviewMouseDown(e);
            return;
        }

        var pointer = e.GetPosition(this);
        var clicked = HitTestCell(pointer);
        if (!clicked.IsValid || Columns[clicked.ColumnIndex] is not IGriddoHostedColumnView)
        {
            base.OnPreviewMouseDown(e);
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var isCtrlPressed = (modifiers & ModifierKeys.Control) != 0;
        var isShiftPressed = (modifiers & ModifierKeys.Shift) != 0;
        var oldCurrentCell = _currentCell;

        if (!isShiftPressed && !isCtrlPressed)
        {
            if (e.ClickCount == 2)
            {
                _selectedCells.Clear();
                _selectedCells.Add(clicked);
                _currentCell = clicked;
                _isDraggingSelection = false;
                _isEditing = false;
                InvalidateVisual();
                base.OnPreviewMouseDown(e);
                return;
            }

            if (e.ClickCount == 1 && oldCurrentCell.IsValid && clicked == oldCurrentCell)
            {
                _selectedCells.Clear();
                _selectedCells.Add(clicked);
                _currentCell = clicked;
                _isDraggingSelection = false;
                _isEditing = false;
                InvalidateVisual();
                base.OnPreviewMouseDown(e);
                return;
            }
        }

        if (isShiftPressed && oldCurrentCell.IsValid && Rows.Count > 0 && Columns.Count > 0)
        {
            SelectRange(oldCurrentCell, clicked, isCtrlPressed);
            _currentCell = clicked;
            _isEditing = false;
            InvalidateVisual();
            base.OnPreviewMouseDown(e);
            return;
        }

        _dragIsAdditive = isCtrlPressed;

        if (_dragIsAdditive)
        {
            _selectedCells.Add(clicked);
        }
        else
        {
            _selectedCells.Clear();
            _selectedCells.Add(clicked);
        }

        _selectionDragSnapshot.Clear();
        _selectionDragSnapshot.UnionWith(_selectedCells);
        _dragAnchorCell = clicked;
        _dragCurrentCell = clicked;
        _isDraggingSelection = false;
        _currentCell = clicked;
        _isEditing = false;
        InvalidateVisual();
        base.OnPreviewMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pointer = e.GetPosition(this);
        if (_isResizingColumn)
        {
            var delta = pointer.X - _resizeStartPoint.X;
            SetColumnWidth(_resizingColumnIndex, _resizeInitialSize + delta);
            InvalidateVisual();
            e.Handled = true;
            base.OnMouseMove(e);
            return;
        }

        if (_isResizingRow)
        {
            var delta = pointer.Y - _resizeStartPoint.Y;
            SetRowHeightKeepingRowTop(_resizingRowIndex, _resizeInitialSize + delta);
            InvalidateVisual();
            e.Handled = true;
            base.OnMouseMove(e);
            return;
        }

        if (_isTrackingColumnMove)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                StopColumnMoveTracking();
                base.OnMouseMove(e);
                return;
            }

            var dragDistance = (pointer - _columnMoveStartPoint).Length;
            var isPointerInColumnHeader = HitTestColumnHeader(pointer) >= 0;
            var shouldShowMovingHeaderCue = isPointerInColumnHeader && dragDistance >= 1;
            if (_isMovingPointerInColumnHeader != shouldShowMovingHeaderCue)
            {
                _isMovingPointerInColumnHeader = shouldShowMovingHeaderCue;
                InvalidateVisual();
            }

            if (!_isMovingColumn)
            {
                if (dragDistance >= 1)
                {
                    _isMovingColumn = true;
                }
            }

            if (_isMovingColumn)
            {
                AutoScrollDuringColumnMove(pointer.X);
                var targetColumn = HitTestColumnHeaderDrag(pointer);
                if (targetColumn >= 0 && targetColumn != _movingColumnIndex)
                {
                    _columnMoveCueIndex = targetColumn;
                    InvalidateVisual();
                }
                else if (_columnMoveCueIndex != -1)
                {
                    _columnMoveCueIndex = -1;
                    InvalidateVisual();
                }
            }

            e.Handled = true;
            base.OnMouseMove(e);
            return;
        }

        if (_isTrackingRowMove)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                StopRowMoveTracking();
                base.OnMouseMove(e);
                return;
            }

            var dragDistance = (pointer - _rowMoveStartPoint).Length;
            if (!_isMovingRow && dragDistance >= 1)
            {
                _isMovingRow = true;
            }

            AutoScrollDuringRowInteraction(pointer.Y);
            var targetRow = HitTestRowHeaderDrag(pointer);
            if (targetRow >= 0 && targetRow != _rowMoveCueIndex)
            {
                _rowMoveCueIndex = targetRow;
                InvalidateVisual();
            }

            e.Handled = true;
            base.OnMouseMove(e);
            return;
        }

        if (_isDraggingColumnHeaderSelection
            && IsMouseCaptured
            && e.LeftButton == MouseButtonState.Pressed)
        {
            AutoScrollDuringColumnMove(pointer.X);
            var hoveredColumnHeader = HitTestColumnHeaderDrag(pointer);
            if (hoveredColumnHeader >= 0 && hoveredColumnHeader != _columnHeaderDragCurrentColumn)
            {
                _columnHeaderDragCurrentColumn = hoveredColumnHeader;
                ApplyColumnHeaderDragSelection();
                InvalidateVisual();
                e.Handled = true;
            }

            base.OnMouseMove(e);
            return;
        }

        if (_isDraggingRowHeaderSelection
            && IsMouseCaptured
            && e.LeftButton == MouseButtonState.Pressed)
        {
            AutoScrollDuringRowInteraction(pointer.Y);
            var hoveredRowHeader = HitTestRowHeaderDrag(pointer);
            if (hoveredRowHeader >= 0 && hoveredRowHeader != _rowHeaderDragCurrentRow)
            {
                _rowHeaderDragCurrentRow = hoveredRowHeader;
                ApplyRowHeaderDragSelection();
                InvalidateVisual();
                e.Handled = true;
            }

            base.OnMouseMove(e);
            return;
        }

        if (!_isDraggingSelection
            || !IsMouseCaptured
            || e.LeftButton != MouseButtonState.Pressed)
        {
            UpdateResizeCursor(pointer);
            base.OnMouseMove(e);
            return;
        }

        AutoScrollDuringCellSelection(pointer);
        var hovered = HitTestCell(pointer);
        if (!hovered.IsValid)
        {
            base.OnMouseMove(e);
            return;
        }

        if (_dragCurrentCell == hovered)
        {
            base.OnMouseMove(e);
            return;
        }

        _dragCurrentCell = hovered;
        ApplyDragSelection();
        InvalidateVisual();
        e.Handled = true;
        base.OnMouseMove(e);
    }

    private void AutoScrollDuringColumnMove(double pointerX)
    {
        if (Columns.Count == 0 || _viewportBodyWidth <= 0)
        {
            return;
        }

        var scrollStart = _rowHeaderWidth + GetFixedColumnsWidth();
        var scrollEnd = _rowHeaderWidth + _viewportBodyWidth;
        if (scrollEnd <= scrollStart)
        {
            return;
        }

        const double edgeBand = 24.0;
        const double maxSpeedPerMove = 48.0;
        var delta = 0.0;
        if (pointerX < scrollStart + edgeBand)
        {
            var pressure = (scrollStart + edgeBand - pointerX) / edgeBand;
            delta = -Math.Clamp(pressure, 0, 3) * maxSpeedPerMove;
        }
        else if (pointerX > scrollEnd - edgeBand)
        {
            var pressure = (pointerX - (scrollEnd - edgeBand)) / edgeBand;
            delta = Math.Clamp(pressure, 0, 3) * maxSpeedPerMove;
        }

        if (Math.Abs(delta) < double.Epsilon)
        {
            return;
        }

        var oldOffset = _horizontalOffset;
        SetHorizontalOffset(_horizontalOffset + delta);
        if (Math.Abs(oldOffset - _horizontalOffset) > double.Epsilon)
        {
            InvalidateVisual();
        }
    }

    private void AutoScrollDuringRowInteraction(double pointerY)
    {
        if (Rows.Count == 0 || _viewportBodyHeight <= 0)
        {
            return;
        }

        var scrollStartY = ScaledColumnHeaderHeight;
        var scrollEndY = ScaledColumnHeaderHeight + _viewportBodyHeight;
        if (scrollEndY <= scrollStartY)
        {
            return;
        }

        const double edgeBand = 24.0;
        const double maxSpeedPerMove = 36.0;
        var delta = 0.0;
        if (pointerY < scrollStartY + edgeBand)
        {
            var pressure = (scrollStartY + edgeBand - pointerY) / edgeBand;
            delta = -Math.Clamp(pressure, 0, 3) * maxSpeedPerMove;
        }
        else if (pointerY > scrollEndY - edgeBand)
        {
            var pressure = (pointerY - (scrollEndY - edgeBand)) / edgeBand;
            delta = Math.Clamp(pressure, 0, 3) * maxSpeedPerMove;
        }

        if (Math.Abs(delta) < double.Epsilon)
        {
            return;
        }

        var oldOffset = _verticalOffset;
        SetVerticalOffset(_verticalOffset + delta);
        if (Math.Abs(oldOffset - _verticalOffset) > double.Epsilon)
        {
            InvalidateVisual();
        }
    }

    private void AutoScrollDuringCellSelection(Point pointer)
    {
        if (Rows.Count == 0 || Columns.Count == 0 || _viewportBodyWidth <= 0 || _viewportBodyHeight <= 0)
        {
            return;
        }

        const double edgeBand = 24.0;
        const double maxHorizontalSpeed = 48.0;
        const double maxVerticalSpeed = 36.0;

        var scrollStartX = _rowHeaderWidth + GetFixedColumnsWidth();
        var scrollEndX = _rowHeaderWidth + _viewportBodyWidth;
        var horizontalDelta = 0.0;
        if (scrollEndX > scrollStartX)
        {
            if (pointer.X < scrollStartX + edgeBand)
            {
                var pressure = (scrollStartX + edgeBand - pointer.X) / edgeBand;
                horizontalDelta = -Math.Clamp(pressure, 0, 3) * maxHorizontalSpeed;
            }
            else if (pointer.X > scrollEndX - edgeBand)
            {
                var pressure = (pointer.X - (scrollEndX - edgeBand)) / edgeBand;
                horizontalDelta = Math.Clamp(pressure, 0, 3) * maxHorizontalSpeed;
            }
        }

        var scrollStartY = ScaledColumnHeaderHeight;
        var scrollEndY = ScaledColumnHeaderHeight + _viewportBodyHeight;
        var verticalDelta = 0.0;
        if (pointer.Y < scrollStartY + edgeBand)
        {
            var pressure = (scrollStartY + edgeBand - pointer.Y) / edgeBand;
            verticalDelta = -Math.Clamp(pressure, 0, 3) * maxVerticalSpeed;
        }
        else if (pointer.Y > scrollEndY - edgeBand)
        {
            var pressure = (pointer.Y - (scrollEndY - edgeBand)) / edgeBand;
            verticalDelta = Math.Clamp(pressure, 0, 3) * maxVerticalSpeed;
        }

        var oldH = _horizontalOffset;
        var oldV = _verticalOffset;
        if (Math.Abs(horizontalDelta) > double.Epsilon)
        {
            SetHorizontalOffset(_horizontalOffset + horizontalDelta);
        }

        if (Math.Abs(verticalDelta) > double.Epsilon)
        {
            SetVerticalOffset(_verticalOffset + verticalDelta);
        }

        if (Math.Abs(oldH - _horizontalOffset) > double.Epsilon || Math.Abs(oldV - _verticalOffset) > double.Epsilon)
        {
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_isTrackingColumnMove && e.ChangedButton == MouseButton.Left)
        {
            if (_isMovingColumn &&
                _movingColumnIndex >= 0 &&
                _columnMoveCueIndex >= 0 &&
                _movingColumnIndex != _columnMoveCueIndex)
            {
                if (_columnMoveStartedFromSelectedHeader)
                {
                    MoveSelectedColumns(_movingColumnIndex, _columnMoveCueIndex);
                }
                else
                {
                    MoveColumn(_movingColumnIndex, _columnMoveCueIndex);
                }
                InvalidateVisual();
            }
            else if (_pendingColumnHeaderSelectionOnMouseUp
                && _pendingColumnHeaderIndex >= 0)
            {
                SelectColumn(_pendingColumnHeaderIndex, _pendingColumnHeaderSelectionAdditive);
                _currentCell = new GriddoCellAddress(
                    Rows.Count == 0 ? 0 : Math.Clamp(_currentCell.RowIndex, 0, Rows.Count - 1),
                    _pendingColumnHeaderIndex);
                _isEditing = false;
                InvalidateVisual();
            }

            StopColumnMoveTracking();
            InvalidateVisual();
            e.Handled = true;
        }

        if (_isTrackingRowMove && e.ChangedButton == MouseButton.Left)
        {
            if (_isMovingRow &&
                _movingRowIndex >= 0 &&
                _rowMoveCueIndex >= 0 &&
                _movingRowIndex != _rowMoveCueIndex)
            {
                MoveSelectedRows(_movingRowIndex, _rowMoveCueIndex);
                InvalidateVisual();
            }
            else if (_pendingRowHeaderSelectionOnMouseUp
                && _pendingRowHeaderIndex >= 0)
            {
                SelectRow(_pendingRowHeaderIndex, _pendingRowHeaderSelectionAdditive);
                _currentCell = new GriddoCellAddress(
                    _pendingRowHeaderIndex,
                    Columns.Count == 0 ? 0 : Math.Clamp(_currentCell.ColumnIndex, 0, Columns.Count - 1));
                _isEditing = false;
                InvalidateVisual();
            }

            StopRowMoveTracking();
            e.Handled = true;
        }

        if (_isResizingColumn && e.ChangedButton == MouseButton.Left)
        {
            _isResizingColumn = false;
            _resizingColumnIndex = -1;
            if (!_isDraggingSelection && IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            e.Handled = true;
        }

        if (_isResizingRow && e.ChangedButton == MouseButton.Left)
        {
            _isResizingRow = false;
            _resizingRowIndex = -1;
            if (!_isDraggingSelection && IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            e.Handled = true;
        }

        if (_isDraggingSelection && e.ChangedButton == MouseButton.Left)
        {
            var shouldActivateHostedEdit =
                _pendingHostedEditActivation
                && _pendingHostedEditCell.IsValid
                && _dragAnchorCell == _pendingHostedEditCell
                && _dragCurrentCell == _pendingHostedEditCell
                && _currentCell == _pendingHostedEditCell;
            _isDraggingSelection = false;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            if (shouldActivateHostedEdit)
            {
                SetCurrentHostedCellEditMode(true);
            }

            _pendingHostedEditActivation = false;
            e.Handled = true;
        }

        if (_isDraggingColumnHeaderSelection && e.ChangedButton == MouseButton.Left)
        {
            _isDraggingColumnHeaderSelection = false;
            _columnHeaderDragAnchorColumn = -1;
            _columnHeaderDragCurrentColumn = -1;
            if (!_isDraggingSelection
                && !_isDraggingRowHeaderSelection
                && !_isResizingColumn
                && !_isResizingRow
                && !_isTrackingColumnMove
                && !_isTrackingRowMove
                && IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            e.Handled = true;
        }

        if (_isDraggingRowHeaderSelection && e.ChangedButton == MouseButton.Left)
        {
            _isDraggingRowHeaderSelection = false;
            _rowHeaderDragAnchorRow = -1;
            _rowHeaderDragCurrentRow = -1;
            if (!_isDraggingSelection && !_isResizingColumn && !_isResizingRow && !_isTrackingColumnMove && IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            e.Handled = true;
        }

        if (e.ChangedButton == MouseButton.Right
            && _currentCell.IsValid
            && _currentCell.RowIndex >= 0
            && _currentCell.RowIndex < Rows.Count
            && _currentCell.ColumnIndex >= 0
            && _currentCell.ColumnIndex < Columns.Count
            && Columns[_currentCell.ColumnIndex] is IGriddoHostedColumnView hostedUp
            && TryGetHostedElement(_currentCell) is FrameworkElement hostUp)
        {
            hostedUp.RelayDirectEditMouseUp(hostUp, e);
            e.Handled = true;
        }

        base.OnMouseUp(e);
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        var ctrlDown = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var inCellEditMode = _isEditing || IsCurrentHostedCellInEditMode();

        // Ctrl+wheel scales the whole grid only when no cell editor is active (hosted Plotto uses Ctrl in-chart).
        if (ctrlDown && !inCellEditMode)
        {
            ContentScale = StepContentScaleStop(ContentScale, e.Delta > 0);
            ShowScaleFeedback();
            e.Handled = true;
            base.OnPreviewMouseWheel(e);
            return;
        }

        // Cell edit mode: wheel (with or without Ctrl) belongs to the editor, not Griddo scale/scroll.
        if (_isEditing)
        {
            e.Handled = true;
        }
        else if (IsCurrentHostedCellInEditMode())
        {
            TryRouteHostedMouseWheelForCell(_currentCell, e);
            e.Handled = true;
        }
        else if (TryRouteHostedMouseWheelZoom(e))
        {
            e.Handled = true;
        }

        base.OnPreviewMouseWheel(e);
    }

    private bool TryRouteHostedMouseWheelForCell(GriddoCellAddress cell, MouseWheelEventArgs e)
    {
        if (!cell.IsValid || cell.ColumnIndex < 0 || cell.ColumnIndex >= Columns.Count)
        {
            return false;
        }

        if (Columns[cell.ColumnIndex] is not IGriddoHostedColumnView hosted)
        {
            return false;
        }

        if (TryGetHostedElement(cell) is not FrameworkElement host)
        {
            return false;
        }

        return hosted.TryHandleHostedMouseWheel(host, e);
    }

    private bool TryRouteHostedMouseWheelZoom(MouseWheelEventArgs e)
    {
        var pos = e.GetPosition(this);
        var cell = HitTestCell(pos);
        if (!cell.IsValid || Columns[cell.ColumnIndex] is not IGriddoHostedColumnView hosted)
        {
            return false;
        }

        if (TryGetHostedElement(cell) is not FrameworkElement host)
        {
            return false;
        }

        return hosted.TryHandleHostedMouseWheel(host, e);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (e.Handled)
        {
            base.OnMouseWheel(e);
            return;
        }

        if (_verticalScrollBar.Maximum <= 0)
        {
            base.OnMouseWheel(e);
            return;
        }

        var delta = e.Delta > 0 ? -GetRowHeight(0) : GetRowHeight(0);
        SetVerticalOffset(_verticalOffset + delta);
        e.Handled = true;
        base.OnMouseWheel(e);
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        if (!TryGetCurrentColumn(out var column))
        {
            return;
        }

        if (!_isEditing)
        {
            if (column is IGriddoHostedColumnView)
            {
                return;
            }

            var ch = e.Text.FirstOrDefault();
            if (ch != default && column.Editor.CanStartWith(ch))
            {
                _editSession.Start(column.Editor.BeginEdit(GetCurrentValue(), ch));
                _isEditing = true;
                InvalidateVisual();
            }

            return;
        }

        _editSession.InsertText(e.Text);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        var isCtrlPressed = (modifiers & ModifierKeys.Control) != 0;
        var isShiftPressed = (modifiers & ModifierKeys.Shift) != 0;
        var isHostedEditing = IsCurrentHostedCellInEditMode();

        if ((isCtrlPressed && e.Key == Key.C) || (isCtrlPressed && e.Key == Key.Insert))
        {
            if (_isEditing)
            {
                CopyEditBufferToClipboard();
            }
            else
            {
                CopySelectedCellsToClipboard();
            }

            e.Handled = true;
            return;
        }

        if ((isCtrlPressed && e.Key == Key.V) || (isShiftPressed && e.Key == Key.Insert))
        {
            if (_isEditing)
            {
                PasteClipboardIntoEditBuffer();
            }
            else
            {
                PasteClipboardIntoGrid();
            }

            e.Handled = true;
            return;
        }

        if ((isCtrlPressed && e.Key == Key.X) || (isShiftPressed && e.Key == Key.Delete))
        {
            if (_isEditing)
            {
                CutEditBufferToClipboard();
            }
            else
            {
                CutSelectedCellsToClipboard();
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && _isEditing)
        {
            CommitEdit();
            MoveCurrentCell(isShiftPressed ? -1 : 1, 0);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && isHostedEditing)
        {
            SetCurrentHostedCellEditMode(false);
            MoveCurrentCell(isShiftPressed ? -1 : 1, 0);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _isEditing)
        {
            _isEditing = false;
            _editSession.Clear();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && isHostedEditing)
        {
            SetCurrentHostedCellEditMode(false);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Tab && _isEditing)
        {
            CommitEdit();
            MoveCurrentCell(0, isShiftPressed ? -1 : 1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Tab && isHostedEditing)
        {
            SetCurrentHostedCellEditMode(false);
            MoveCurrentCell(0, isShiftPressed ? -1 : 1);
            e.Handled = true;
            return;
        }

        if (_isEditing)
        {
            switch (e.Key)
            {
                case Key.Left:
                    _editSession.MoveCaretLeft(isCtrlPressed, isShiftPressed);
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                case Key.Right:
                    _editSession.MoveCaretRight(isCtrlPressed, isShiftPressed);
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                case Key.Home:
                    _editSession.MoveCaretHome(isShiftPressed);
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                case Key.End:
                    _editSession.MoveCaretEnd(isShiftPressed);
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                case Key.Back:
                    if (_editSession.Backspace())
                    {
                        InvalidateVisual();
                    }
                    e.Handled = true;
                    return;
                case Key.Delete:
                    if (_editSession.DeleteForward())
                    {
                        InvalidateVisual();
                    }

                    e.Handled = true;
                    return;
                case Key.Up:
                case Key.Down:
                    e.Handled = true;
                    return;
            }
        }

        if (!_isEditing)
        {
            if (e.Key == Key.Enter)
            {
                MoveCurrentCell(isShiftPressed ? -1 : 1, 0);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Tab)
            {
                MoveCurrentCell(0, isShiftPressed ? -1 : 1);
                e.Handled = true;
                return;
            }

            if (HandleCellKeyboardNavigation(e.Key, isCtrlPressed, isShiftPressed))
            {
                e.Handled = true;
                return;
            }

            switch (e.Key)
            {
                case Key.F2:
                    BeginCurrentCellEdit();
                    e.Handled = true;
                    break;
                case Key.Delete:
                    ClearSelectedCells();
                    e.Handled = true;
                    break;
            }
        }

        base.OnKeyDown(e);
    }

    private void DrawColumnHeader(DrawingContext dc, int col, double x, Typeface typeface)
    {
        var width = GetColumnWidth(col);
        var rect = new Rect(x, 0, width, ScaledColumnHeaderHeight);
        var headerBackground = IsColumnSelected(col) ? SelectionBackground : HeaderBackground;
        dc.DrawRectangle(headerBackground, null, rect);
        var pen = new Pen(GridLineBrush, GridPenThickness);
        // Top edge of header strip is drawn once in DrawOuterWorksheetFrame (matches DrawLine rasterization for scroll columns).
        dc.DrawLine(pen, rect.TopRight, rect.BottomRight);
        if (col == 0)
        {
            dc.DrawLine(pen, rect.TopLeft, rect.BottomLeft);
        }
        var headerText = new FormattedText(
            Columns[col].Header,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            EffectiveFontSize,
            Brushes.Black,
            1.0);
        headerText.SetFontWeight(FontWeights.Bold);
        headerText.MaxTextWidth = Math.Max(1, rect.Width - 8);
        headerText.MaxTextHeight = Math.Max(1, rect.Height - 4);
        headerText.Trimming = TextTrimming.CharacterEllipsis;
        var headerY = rect.Y + Math.Max(0, (rect.Height - headerText.Height) / 2);
        dc.DrawText(headerText, new Point(rect.X + 4, headerY));

        if (_fixedColumnCount > 0 && col == _fixedColumnCount - 1)
        {
            dc.DrawLine(
                new Pen(FixedColumnRightBorderBrush, GridPenThickness),
                new Point(rect.Right, rect.Top),
                new Point(rect.Right, rect.Bottom));
        }
    }

    private int HitTestRowHeaderDrag(Point point)
    {
        if (Rows.Count == 0 || _viewportBodyHeight <= 0)
        {
            return -1;
        }

        var minY = ScaledColumnHeaderHeight;
        var maxY = ScaledColumnHeaderHeight + _viewportBodyHeight - 1;
        var clampedY = Math.Clamp(point.Y, minY, maxY);
        var contentY = clampedY - ScaledColumnHeaderHeight + _verticalOffset;
        var row = HitTestRowIndex(contentY);
        return row >= 0 ? row : -1;
    }

    private int HitTestColumnHeaderDrag(Point point)
    {
        if (Columns.Count == 0 || _viewportBodyWidth <= 0)
        {
            return -1;
        }

        var minX = _rowHeaderWidth;
        var maxX = _rowHeaderWidth + _viewportBodyWidth - 1;
        var clampedX = Math.Clamp(point.X, minX, maxX);
        return HitTestColumnHeader(new Point(clampedX, point.Y));
    }

    private void DrawBodyCell(
        DrawingContext dc,
        int row,
        int col,
        double x,
        double y,
        double rowHeight,
        object rowData,
        Rect bodyViewport,
        Typeface typeface)
    {
        var colWidth = GetColumnWidth(col);
        var rect = new Rect(x, y, colWidth, rowHeight);
        var address = new GriddoCellAddress(row, col);

        var isHostedCellEditing = IsHostedCellInEditMode(address);
        if (_selectedCells.Contains(address) && !isHostedCellEditing)
        {
            dc.DrawRectangle(SelectionBackground, null, rect);
        }

        dc.DrawRectangle(null, new Pen(GridLineBrush, GridPenThickness), rect);
        if (_fixedColumnCount > 0 && col == _fixedColumnCount - 1)
        {
            dc.DrawLine(
                new Pen(FixedColumnRightBorderBrush, GridPenThickness),
                new Point(rect.Right, rect.Top),
                new Point(rect.Right, rect.Bottom));
        }

        if (Columns[col] is IGriddoHostedColumnView)
        {
            return;
        }

        var value = Columns[col].GetValue(rowData);
        var isGraphic = value is ImageSource or Geometry;
        // Intersect with viewport so HTML (and plain text) centers in the visible strip when the row is clipped vertically.
        var paintBounds = isGraphic ? rect : Rect.Intersect(rect, bodyViewport);
        if (!paintBounds.IsEmpty)
        {
            GriddoValuePainter.Paint(
                dc,
                value,
                paintBounds,
                typeface,
                EffectiveFontSize,
                Brushes.Black,
                Columns[col].IsHtml,
                true,
                Columns[col].ContentAlignment,
                isGraphic ? VerticalAlignment.Top : VerticalAlignment.Center);
        }
    }

    private void DrawHeaders(DrawingContext dc)
    {
        // Fixed top-left corner header cell (row-header column title area). Outer top/left strokes live in DrawOuterWorksheetFrame only.
        var cornerRect = new Rect(0, 0, _rowHeaderWidth, ScaledColumnHeaderHeight);
        dc.DrawRectangle(HeaderBackground, null, cornerRect);

        var typeface = new Typeface("Segoe UI");
        var fixedW = GetFixedColumnsWidth();
        var scrollLeft = _rowHeaderWidth + fixedW;

        if (_fixedColumnCount < Columns.Count && scrollLeft < _rowHeaderWidth + _viewportBodyWidth)
        {
            var scrollClipW = Math.Max(0, _viewportBodyWidth - fixedW);
            var scrollClip = new Rect(scrollLeft, 0, scrollClipW, ScaledColumnHeaderHeight);
            dc.PushClip(new RectangleGeometry(scrollClip));
            GetVisibleScrollColumnRange(out var sCol, out var eCol, out var x);
            if (eCol >= sCol)
            {
                for (var col = sCol; col <= eCol; col++)
                {
                    DrawColumnHeader(dc, col, x, typeface);
                    x += GetColumnWidth(col);
                }
            }

            dc.Pop();
        }

        if (_fixedColumnCount > 0)
        {
            var fixedClipW = Math.Min(fixedW, _viewportBodyWidth);
            var fixedClip = new Rect(_rowHeaderWidth, 0, fixedClipW, ScaledColumnHeaderHeight);
            dc.PushClip(new RectangleGeometry(fixedClip));
            var fx = _rowHeaderWidth;
            for (var col = 0; col < _fixedColumnCount; col++)
            {
                DrawColumnHeader(dc, col, fx, typeface);
                fx += GetColumnWidth(col);
            }

            dc.Pop();
        }

        DrawColumnMoveCue(dc);
        var rowHeaderClip = new Rect(0, ScaledColumnHeaderHeight, _rowHeaderWidth, _viewportBodyHeight);
        dc.PushClip(new RectangleGeometry(rowHeaderClip));
        {
            GetVisibleRowRange(out var startRow, out var endRow);
            var rowHeight = GetRowHeight(0);
            var y = ScaledColumnHeaderHeight + (startRow * rowHeight) - _verticalOffset;
            var rowHeaderPen = new Pen(GridLineBrush, GridPenThickness);
            for (var row = startRow; row <= endRow; row++)
            {
                var rect = new Rect(0, y, _rowHeaderWidth, rowHeight);
                var rowHeaderBackground = IsRowSelected(row) ? SelectionBackground : HeaderBackground;
                dc.DrawRectangle(rowHeaderBackground, null, rect);
                // Top + bottom only; outer x=0 edge is one DrawLine in DrawOuterWorksheetFrame (avoids path vs line mismatch).
                dc.DrawLine(rowHeaderPen, new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top));
                dc.DrawLine(rowHeaderPen, new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Bottom));
                var visibleRect = Rect.Intersect(rect, rowHeaderClip);
                if (!visibleRect.IsEmpty)
                {
                    GriddoValuePainter.Paint(dc, row + 1, visibleRect, typeface, EffectiveFontSize, Brushes.Black, false, false, TextAlignment.Right, VerticalAlignment.Center);
                }

                y += rowHeight;
            }
        }
        dc.Pop();

        DrawOuterWorksheetFrame(dc);
    }

    /// <summary>
    /// Single DrawLine for the outermost grid edges so they match column header strokes (PathGeometry stroke looked thicker under AA).
    /// </summary>
    private void DrawOuterWorksheetFrame(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var pen = new Pen(GridLineBrush, GridPenThickness);
        var topRight = Math.Max(0, ActualWidth - ScrollBarSize);
        var stripBottom = ScaledColumnHeaderHeight + Math.Max(0, _viewportBodyHeight);
        var layoutBottom = Math.Max(0, ActualHeight - ScrollBarSize);
        var leftBottom = Math.Min(stripBottom, layoutBottom);
        var rightX = Math.Max(0, ActualWidth - 1);
        var bottomY = Math.Max(0, ActualHeight - 1);
        dc.DrawLine(pen, new Point(0, 0), new Point(topRight, 0));
        dc.DrawLine(pen, new Point(0, 0), new Point(0, leftBottom));
        dc.DrawLine(pen, new Point(rightX, 0), new Point(rightX, bottomY));
        dc.DrawLine(pen, new Point(0, bottomY), new Point(rightX, bottomY));
    }

    private void DrawBody(DrawingContext dc)
    {
        var bodyViewport = new Rect(_rowHeaderWidth, ScaledColumnHeaderHeight, _viewportBodyWidth, _viewportBodyHeight);
        var typeface = new Typeface("Segoe UI");
        GetVisibleRowRange(out var startRow, out var endRow);
        if (endRow < startRow)
        {
            return;
        }

        var rowHeight = GetRowHeight(0);
        var fixedW = GetFixedColumnsWidth();
        var scrollLeft = _rowHeaderWidth + fixedW;

        if (_fixedColumnCount < Columns.Count && scrollLeft < _rowHeaderWidth + _viewportBodyWidth)
        {
            var scrollClipW = Math.Max(0, _viewportBodyWidth - fixedW);
            var scrollClip = new Rect(scrollLeft, ScaledColumnHeaderHeight, scrollClipW, _viewportBodyHeight);
            dc.PushClip(new RectangleGeometry(scrollClip));
            GetVisibleScrollColumnRange(out var sCol, out var eCol, out var startX);
            if (eCol >= sCol)
            {
                for (var row = startRow; row <= endRow; row++)
                {
                    var y = ScaledColumnHeaderHeight + (row * rowHeight) - _verticalOffset;
                    var rowData = Rows[row];
                    var x = startX;
                    for (var col = sCol; col <= eCol; col++)
                    {
                        DrawBodyCell(dc, row, col, x, y, rowHeight, rowData, bodyViewport, typeface);
                        x += GetColumnWidth(col);
                    }
                }
            }

            dc.Pop();
        }

        if (_fixedColumnCount > 0)
        {
            var fixedClipW = Math.Min(fixedW, _viewportBodyWidth);
            var fixedClip = new Rect(_rowHeaderWidth, ScaledColumnHeaderHeight, fixedClipW, _viewportBodyHeight);
            dc.PushClip(new RectangleGeometry(fixedClip));
            for (var row = startRow; row <= endRow; row++)
            {
                var y = ScaledColumnHeaderHeight + (row * rowHeight) - _verticalOffset;
                var rowData = Rows[row];
                var x = _rowHeaderWidth;
                for (var col = 0; col < _fixedColumnCount; col++)
                {
                    DrawBodyCell(dc, row, col, x, y, rowHeight, rowData, bodyViewport, typeface);
                    x += GetColumnWidth(col);
                }
            }

            dc.Pop();
        }
    }

    private void DrawCurrentCellOverlay(DrawingContext dc)
    {
        if (!_currentCell.IsValid)
        {
            return;
        }

        var rect = GetCellRect(_currentCell.RowIndex, _currentCell.ColumnIndex);
        if (rect.IsEmpty)
        {
            return;
        }

        dc.PushClip(new RectangleGeometry(GetColumnBodyBandClipRect(_currentCell.ColumnIndex)));

        const double currentCellInset = 0.5;
        var insetRect = new Rect(
            rect.X + currentCellInset,
            rect.Y + currentCellInset,
            Math.Max(0, rect.Width - (currentCellInset * 2)),
            Math.Max(0, rect.Height - (currentCellInset * 2)));
        var isHostedEditMode = IsCurrentHostedCellInEditMode();
        var borderBrush = (_isEditing || isHostedEditMode) ? Brushes.Red : CurrentCellBorderBrush;
        dc.DrawRectangle(null, new Pen(borderBrush, ScaledCurrentCellBorder), insetRect);
        dc.Pop();
    }

    private bool IsCurrentHostedCellInEditMode()
    {
        if (_currentCell.RowIndex < 0 || _currentCell.RowIndex >= Rows.Count || _currentCell.ColumnIndex < 0 || _currentCell.ColumnIndex >= Columns.Count)
        {
            return false;
        }

        if (Columns[_currentCell.ColumnIndex] is not IGriddoHostedColumnView hostedColumn)
        {
            return false;
        }

        return TryGetHostedElement(_currentCell) is FrameworkElement host && hostedColumn.IsHostInEditMode(host);
    }

    private bool IsHostedCellInEditMode(GriddoCellAddress cell)
    {
        if (cell.RowIndex < 0 || cell.RowIndex >= Rows.Count || cell.ColumnIndex < 0 || cell.ColumnIndex >= Columns.Count)
        {
            return false;
        }

        if (Columns[cell.ColumnIndex] is not IGriddoHostedColumnView hostedColumn)
        {
            return false;
        }

        return TryGetHostedElement(cell) is FrameworkElement host && hostedColumn.IsHostInEditMode(host);
    }

    private void SetCurrentHostedCellEditMode(bool isEditing)
    {
        if (_currentCell.RowIndex < 0 || _currentCell.RowIndex >= Rows.Count || _currentCell.ColumnIndex < 0 || _currentCell.ColumnIndex >= Columns.Count)
        {
            return;
        }

        if (Columns[_currentCell.ColumnIndex] is not IGriddoHostedColumnView hostedColumn)
        {
            return;
        }

        if (TryGetHostedElement(_currentCell) is not FrameworkElement host)
        {
            return;
        }

        hostedColumn.SetHostEditMode(host, isEditing);
        InvalidateVisual();
    }

    private void BeginCurrentCellEdit()
    {
        if (_currentCell.RowIndex < 0 || _currentCell.RowIndex >= Rows.Count || _currentCell.ColumnIndex < 0 || _currentCell.ColumnIndex >= Columns.Count)
        {
            return;
        }

        if (Columns[_currentCell.ColumnIndex] is IGriddoHostedColumnView)
        {
            SetCurrentHostedCellEditMode(true);
            return;
        }

        BeginEditWithoutReplacing();
    }

    private void DrawEditingText(DrawingContext dc)
    {
        if (!_isEditing || !_currentCell.IsValid)
        {
            return;
        }

        var rect = GetCellRect(_currentCell.RowIndex, _currentCell.ColumnIndex);
        if (rect.IsEmpty)
        {
            return;
        }

        if (!TryGetCurrentColumn(out var column))
        {
            return;
        }

        dc.PushClip(new RectangleGeometry(GetColumnBodyBandClipRect(_currentCell.ColumnIndex)));

        // Keep editor visuals inside the cell border so the edit outline thickness stays consistent.
        const double editContentInset = 1.0;
        var editContentRect = new Rect(
            rect.X + editContentInset,
            rect.Y + editContentInset,
            Math.Max(0, rect.Width - (editContentInset * 2)),
            Math.Max(0, rect.Height - (editContentInset * 2)));

        if (column.IsHtml)
        {
            var bodyCellsViewport = new Rect(_rowHeaderWidth, ScaledColumnHeaderHeight, _viewportBodyWidth, _viewportBodyHeight);
            editContentRect = Rect.Intersect(editContentRect, bodyCellsViewport);
        }

        if (editContentRect.IsEmpty)
        {
            dc.Pop();
            return;
        }

        dc.DrawRectangle(Brushes.White, null, editContentRect);
        var typeface = new Typeface("Segoe UI");
        var fontSize = EffectiveFontSize;
        var verticalAlignment = VerticalAlignment.Center;
        GriddoValuePainter.Paint(dc, _editSession.Buffer, editContentRect, typeface, fontSize, Brushes.Black, column.IsHtml, true, column.ContentAlignment, verticalAlignment);

        var displayText = _editSession.Buffer;
        var editText = new FormattedText(
            displayText,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            1.0);
        editText.TextAlignment = column.ContentAlignment;
        editText.MaxTextWidth = Math.Max(1, editContentRect.Width - 8);
        editText.MaxTextHeight = Math.Max(1, editContentRect.Height - 4);
        editText.Trimming = TextTrimming.CharacterEllipsis;
        var caretOriginY = verticalAlignment == VerticalAlignment.Center
            ? editContentRect.Y + Math.Max(0, (editContentRect.Height - editText.Height) / 2)
            : editContentRect.Y + 2;
        var prefixText = displayText[.._editSession.CaretIndex];
        var prefixFormattedText = new FormattedText(
            prefixText,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            1.0);
        var contentWidth = Math.Max(1, editContentRect.Width - 8);
        var totalTextWidth = Math.Min(editText.WidthIncludingTrailingWhitespace, contentWidth);
        var prefixWidth = Math.Min(prefixFormattedText.WidthIncludingTrailingWhitespace, contentWidth);
        var textStartX = editContentRect.X + 4;
        if (column.ContentAlignment == TextAlignment.Right)
        {
            textStartX += Math.Max(0, contentWidth - totalTextWidth);
        }
        else if (column.ContentAlignment == TextAlignment.Center)
        {
            textStartX += Math.Max(0, (contentWidth - totalTextWidth) / 2);
        }

        if (_editSession.TryGetSelection(out var selectionStart, out var selectionEnd))
        {
            var beforeSelection = displayText[..selectionStart];
            var selectedText = displayText[selectionStart..selectionEnd];
            var beforeWidth = new FormattedText(
                beforeSelection,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.Black,
                1.0).WidthIncludingTrailingWhitespace;
            var selectedWidth = new FormattedText(
                selectedText,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.Black,
                1.0).WidthIncludingTrailingWhitespace;
            var selectionX = Math.Clamp(textStartX + Math.Min(beforeWidth, contentWidth), editContentRect.X + 2, editContentRect.Right - 2);
            var selectionRight = Math.Clamp(selectionX + Math.Min(selectedWidth, contentWidth), editContentRect.X + 2, editContentRect.Right - 2);
            if (selectionRight > selectionX)
            {
                var selectionRect = new Rect(selectionX, caretOriginY, selectionRight - selectionX, Math.Max(1, editText.Height));
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(120, 102, 178, 255)), null, selectionRect);
                GriddoValuePainter.Paint(dc, _editSession.Buffer, editContentRect, typeface, fontSize, Brushes.Black, column.IsHtml, true, column.ContentAlignment, verticalAlignment);
            }
        }

        var caretX = textStartX + prefixWidth;
        caretX = Math.Clamp(caretX, editContentRect.X + 2, editContentRect.Right - 2);
        var caretTop = caretOriginY;
        var caretBottom = Math.Min(editContentRect.Bottom - 2, caretOriginY + editText.Height);
        if (caretBottom > caretTop)
        {
            dc.DrawLine(new Pen(Brushes.Black, 1), new Point(caretX, caretTop), new Point(caretX, caretBottom));
        }

        dc.Pop();
    }

    private bool TryGetCurrentColumn(out IGriddoColumnView column)
    {
        if (_currentCell.ColumnIndex < 0 || _currentCell.ColumnIndex >= Columns.Count)
        {
            column = default!;
            return false;
        }

        column = Columns[_currentCell.ColumnIndex];
        return true;
    }

    private object? GetCurrentValue()
    {
        if (_currentCell.RowIndex < 0 || _currentCell.RowIndex >= Rows.Count || !TryGetCurrentColumn(out var column))
        {
            return null;
        }

        return column.GetValue(Rows[_currentCell.RowIndex]);
    }

    private void BeginEditWithoutReplacing()
    {
        if (!TryGetCurrentColumn(out var column))
        {
            return;
        }

        if (column is IGriddoHostedColumnView)
        {
            return;
        }

        _editSession.Start(column.Editor.BeginEdit(GetCurrentValue()));
        _isEditing = true;
        InvalidateVisual();
    }

    private void CommitEdit()
    {
        if (!_isEditing || !TryGetCurrentColumn(out var column))
        {
            return;
        }

        if (_currentCell.RowIndex < 0 || _currentCell.RowIndex >= Rows.Count)
        {
            return;
        }

        if (column.Editor.TryCommit(_editSession.Buffer, out var newValue))
        {
            column.TrySetValue(Rows[_currentCell.RowIndex], newValue);
        }

        _isEditing = false;
        _editSession.Clear();
        InvalidateVisual();
    }

    private void InsertIntoEditBuffer(string text)
    {
        _editSession.InsertText(text);
    }

    private void PasteClipboardIntoEditBuffer()
    {
        var text = _editSession.GetSanitizedClipboardText();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        _editSession.InsertText(text);
        InvalidateVisual();
    }

    private void CopyEditBufferToClipboard()
    {
        Clipboard.SetText(_editSession.GetCopyText());
    }

    private void CutEditBufferToClipboard()
    {
        Clipboard.SetText(_editSession.CutText());
        InvalidateVisual();
    }

    private void CutSelectedCellsToClipboard()
    {
        CopySelectedCellsToClipboard();
        ClearSelectedCells();
    }

    private void ClearSelectedCells()
    {
        if (Rows.Count == 0 || Columns.Count == 0)
        {
            return;
        }

        var targetCells = _selectedCells.Count > 0
            ? _selectedCells.ToList()
            : [_currentCell];
        var didChange = false;
        foreach (var address in targetCells)
        {
            if (address.RowIndex < 0 || address.RowIndex >= Rows.Count || address.ColumnIndex < 0 || address.ColumnIndex >= Columns.Count)
            {
                continue;
            }

            var column = Columns[address.ColumnIndex];
            var row = Rows[address.RowIndex];
            if (column.Editor.TryCommit(string.Empty, out var emptyValue) && column.TrySetValue(row, emptyValue))
            {
                didChange = true;
                continue;
            }

            if (column.TrySetValue(row, null))
            {
                didChange = true;
            }
        }

        if (didChange)
        {
            InvalidateVisual();
        }
    }

    private void MoveCurrentCell(int rowDelta, int colDelta)
    {
        if (Rows.Count == 0 || Columns.Count == 0)
        {
            return;
        }

        var row = Math.Clamp(_currentCell.RowIndex + rowDelta, 0, Rows.Count - 1);
        var col = Math.Clamp(_currentCell.ColumnIndex + colDelta, 0, Columns.Count - 1);
        _currentCell = new GriddoCellAddress(row, col);
        _selectedCells.Clear();
        _selectedCells.Add(_currentCell);
        InvalidateVisual();
    }

    private bool HandleCellKeyboardNavigation(Key key, bool isCtrlPressed, bool isShiftPressed)
    {
        if (!GriddoCellNavigator.TryGetTarget(key, isCtrlPressed, _currentCell, Rows.Count, Columns.Count, out var target))
        {
            return false;
        }

        if (isShiftPressed)
        {
            if (!_hasKeyboardSelectionAnchor)
            {
                _keyboardSelectionAnchor = _currentCell;
                _hasKeyboardSelectionAnchor = true;
            }

            _currentCell = target;
            SelectRange(_keyboardSelectionAnchor, _currentCell, additive: false);
            InvalidateVisual();
            return true;
        }

        _hasKeyboardSelectionAnchor = false;
        _currentCell = target;
        _selectedCells.Clear();
        _selectedCells.Add(_currentCell);
        InvalidateVisual();
        return true;
    }

    private void ApplyDragSelection()
    {
        _selectedCells.Clear();
        if (_dragIsAdditive || (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _selectedCells.UnionWith(_selectionDragSnapshot);
        }

        var minRow = Math.Min(_dragAnchorCell.RowIndex, _dragCurrentCell.RowIndex);
        var maxRow = Math.Max(_dragAnchorCell.RowIndex, _dragCurrentCell.RowIndex);
        var minCol = Math.Min(_dragAnchorCell.ColumnIndex, _dragCurrentCell.ColumnIndex);
        var maxCol = Math.Max(_dragAnchorCell.ColumnIndex, _dragCurrentCell.ColumnIndex);

        for (var row = minRow; row <= maxRow; row++)
        {
            for (var col = minCol; col <= maxCol; col++)
            {
                _selectedCells.Add(new GriddoCellAddress(row, col));
            }
        }
    }

    private void SelectRange(GriddoCellAddress from, GriddoCellAddress to, bool additive)
    {
        if (!additive)
        {
            _selectedCells.Clear();
        }

        var minRow = Math.Min(from.RowIndex, to.RowIndex);
        var maxRow = Math.Max(from.RowIndex, to.RowIndex);
        var minCol = Math.Min(from.ColumnIndex, to.ColumnIndex);
        var maxCol = Math.Max(from.ColumnIndex, to.ColumnIndex);
        for (var row = minRow; row <= maxRow; row++)
        {
            for (var col = minCol; col <= maxCol; col++)
            {
                if (row >= 0 && row < Rows.Count && col >= 0 && col < Columns.Count)
                {
                    _selectedCells.Add(new GriddoCellAddress(row, col));
                }
            }
        }
    }

    private void SelectColumn(int columnIndex, bool additive)
    {
        if (columnIndex < 0 || columnIndex >= Columns.Count)
        {
            return;
        }

        if (!additive)
        {
            _selectedCells.Clear();
        }

        for (var row = 0; row < Rows.Count; row++)
        {
            _selectedCells.Add(new GriddoCellAddress(row, columnIndex));
        }
    }

    private void SelectAllCells()
    {
        _selectedCells.Clear();
        if (Rows.Count == 0 || Columns.Count == 0)
        {
            return;
        }

        for (var row = 0; row < Rows.Count; row++)
        {
            for (var col = 0; col < Columns.Count; col++)
            {
                _selectedCells.Add(new GriddoCellAddress(row, col));
            }
        }
    }

    private void StopColumnMoveTracking()
    {
        _isTrackingColumnMove = false;
        _isMovingColumn = false;
        _isMovingPointerInColumnHeader = false;
        _columnMoveStartedFromSelectedHeader = false;
        _pendingColumnHeaderSelectionOnMouseUp = false;
        _pendingColumnHeaderIndex = -1;
        _movingColumnIndex = -1;
        _columnMoveCueIndex = -1;
        if (!_isDraggingSelection && !_isResizingColumn && !_isResizingRow && !_isTrackingRowMove && IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }

    private void StopRowMoveTracking()
    {
        _isTrackingRowMove = false;
        _isMovingRow = false;
        _movingRowIndex = -1;
        _rowMoveCueIndex = -1;
        _pendingRowHeaderSelectionOnMouseUp = false;
        _pendingRowHeaderIndex = -1;
        if (!_isDraggingSelection && !_isResizingColumn && !_isResizingRow && !_isTrackingColumnMove && IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }

    private void MoveColumn(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || toIndex < 0 || fromIndex >= Columns.Count || toIndex >= Columns.Count || fromIndex == toIndex)
        {
            return;
        }

        Columns.Move(fromIndex, toIndex);

        var oldCurrent = _currentCell;
        _currentCell = new GriddoCellAddress(oldCurrent.RowIndex, RemapColumnIndex(oldCurrent.ColumnIndex, fromIndex, toIndex));

        var remapped = new HashSet<GriddoCellAddress>();
        foreach (var address in _selectedCells)
        {
            remapped.Add(new GriddoCellAddress(address.RowIndex, RemapColumnIndex(address.ColumnIndex, fromIndex, toIndex)));
        }

        _selectedCells.Clear();
        _selectedCells.UnionWith(remapped);
    }

    private void MoveSelectedColumns(int anchorColumn, int targetColumn)
    {
        var selected = GetSelectedColumnIndices();
        if (selected.Count <= 1 || !selected.Contains(anchorColumn))
        {
            MoveColumn(anchorColumn, targetColumn);
            return;
        }

        var minSelected = selected[0];
        var maxSelected = selected[^1];
        var insertAfterTarget = targetColumn > maxSelected;
        if (targetColumn >= minSelected && targetColumn <= maxSelected)
        {
            return;
        }

        var oldToNew = MoveColumnOrRowIndices(
            Columns.Count,
            selected,
            targetColumn,
            insertAfterTarget,
            index => Columns.Move(index.from, index.to));
        RemapSelectionAfterColumnMove(oldToNew);
    }

    private void MoveSelectedRows(int anchorRow, int targetRow)
    {
        var selected = GetSelectedRowIndices();
        if (selected.Count == 0 || !selected.Contains(anchorRow))
        {
            return;
        }

        var minSelected = selected[0];
        var maxSelected = selected[^1];
        var insertAfterTarget = targetRow > maxSelected;
        if (targetRow >= minSelected && targetRow <= maxSelected)
        {
            return;
        }

        var oldToNew = MoveColumnOrRowIndices(
            Rows.Count,
            selected,
            targetRow,
            insertAfterTarget,
            index => Rows.Move(index.from, index.to));
        RemapSelectionAfterRowMove(oldToNew);
    }

    private static int[] MoveColumnOrRowIndices(
        int count,
        List<int> selectedIndices,
        int targetIndex,
        bool insertAfterTarget,
        Action<(int from, int to)> mover)
    {
        if (count <= 0 || selectedIndices.Count == 0)
        {
            return [];
        }

        selectedIndices.Sort();
        var selectedLookup = selectedIndices.ToHashSet();
        var unselected = Enumerable.Range(0, count)
            .Where(i => !selectedLookup.Contains(i))
            .ToList();
        var insertionPoint = insertAfterTarget
            ? unselected.Count(i => i <= targetIndex)
            : unselected.Count(i => i < targetIndex);
        insertionPoint = Math.Clamp(insertionPoint, 0, unselected.Count);

        var finalOrder = new List<int>(count);
        finalOrder.AddRange(unselected.Take(insertionPoint));
        finalOrder.AddRange(selectedIndices);
        finalOrder.AddRange(unselected.Skip(insertionPoint));

        var currentOrder = Enumerable.Range(0, count).ToList();
        for (var newIndex = 0; newIndex < finalOrder.Count; newIndex++)
        {
            var item = finalOrder[newIndex];
            var currentIndex = currentOrder.IndexOf(item);
            if (currentIndex < 0 || currentIndex == newIndex)
            {
                continue;
            }

            mover((currentIndex, newIndex));
            currentOrder.RemoveAt(currentIndex);
            currentOrder.Insert(newIndex, item);
        }

        var oldToNew = new int[count];
        for (var newIndex = 0; newIndex < finalOrder.Count; newIndex++)
        {
            oldToNew[finalOrder[newIndex]] = newIndex;
        }

        return oldToNew;
    }

    private void RemapSelectionAfterColumnMove(int[] oldToNew)
    {
        if (oldToNew.Length == 0)
        {
            return;
        }

        if (_currentCell.IsValid
            && _currentCell.ColumnIndex >= 0
            && _currentCell.ColumnIndex < oldToNew.Length)
        {
            _currentCell = new GriddoCellAddress(_currentCell.RowIndex, oldToNew[_currentCell.ColumnIndex]);
        }

        var remapped = new HashSet<GriddoCellAddress>();
        foreach (var address in _selectedCells)
        {
            if (address.ColumnIndex >= 0 && address.ColumnIndex < oldToNew.Length)
            {
                remapped.Add(new GriddoCellAddress(address.RowIndex, oldToNew[address.ColumnIndex]));
            }
        }

        _selectedCells.Clear();
        _selectedCells.UnionWith(remapped);
    }

    private void RemapSelectionAfterRowMove(int[] oldToNew)
    {
        if (oldToNew.Length == 0)
        {
            return;
        }

        if (_currentCell.IsValid
            && _currentCell.RowIndex >= 0
            && _currentCell.RowIndex < oldToNew.Length)
        {
            _currentCell = new GriddoCellAddress(oldToNew[_currentCell.RowIndex], _currentCell.ColumnIndex);
        }

        var remapped = new HashSet<GriddoCellAddress>();
        foreach (var address in _selectedCells)
        {
            if (address.RowIndex >= 0 && address.RowIndex < oldToNew.Length)
            {
                remapped.Add(new GriddoCellAddress(oldToNew[address.RowIndex], address.ColumnIndex));
            }
        }

        _selectedCells.Clear();
        _selectedCells.UnionWith(remapped);
    }

    private static int RemapColumnIndex(int columnIndex, int fromIndex, int toIndex)
    {
        if (columnIndex == fromIndex)
        {
            return toIndex;
        }

        if (fromIndex < toIndex)
        {
            return (columnIndex > fromIndex && columnIndex <= toIndex) ? columnIndex - 1 : columnIndex;
        }

        return (columnIndex >= toIndex && columnIndex < fromIndex) ? columnIndex + 1 : columnIndex;
    }

    private void DrawColumnMoveCue(DrawingContext dc)
    {
        if (!_isTrackingColumnMove)
        {
            return;
        }

        var clipRect = new Rect(_rowHeaderWidth, 0, _viewportBodyWidth, ScaledColumnHeaderHeight);

        // Keep a thin red "current/source" marker on the column(s) being moved.
        if (_isMovingPointerInColumnHeader && _movingColumnIndex >= 0 && _movingColumnIndex < Columns.Count)
        {
            var currentPen = new Pen(Brushes.Red, 1);
            var movingColumns = _columnMoveStartedFromSelectedHeader && _isMovingColumn
                ? GetSelectedColumnIndices()
                : [_movingColumnIndex];
            foreach (var movingColumn in movingColumns)
            {
                if (movingColumn < 0 || movingColumn >= Columns.Count)
                {
                    continue;
                }

                var movingRect = GetColumnHeaderRect(movingColumn);
                var visibleMovingRect = Rect.Intersect(movingRect, clipRect);
                if (visibleMovingRect.IsEmpty)
                {
                    continue;
                }

                var currentRect = new Rect(
                    visibleMovingRect.X + 0.5,
                    visibleMovingRect.Y + 0.5,
                    Math.Max(0, visibleMovingRect.Width - 1),
                    Math.Max(0, visibleMovingRect.Height - 1));
                dc.DrawRectangle(null, currentPen, currentRect);
            }
        }

        if (_columnMoveCueIndex < 0 || _columnMoveCueIndex >= Columns.Count)
        {
            return;
        }

        var cueRect = GetColumnHeaderRect(_columnMoveCueIndex);
        if (cueRect.IsEmpty)
        {
            return;
        }

        var visibleCueRect = Rect.Intersect(cueRect, clipRect);
        if (visibleCueRect.IsEmpty)
        {
            return;
        }

        var x = visibleCueRect.Left;
        if (_columnMoveStartedFromSelectedHeader && _isMovingColumn)
        {
            var selectedColumns = GetSelectedColumnIndices();
            if (selectedColumns.Count > 0)
            {
                var minSelected = selectedColumns[0];
                var maxSelected = selectedColumns[^1];
                var movingLeft = _columnMoveCueIndex < minSelected;
                var movingRight = _columnMoveCueIndex > maxSelected;
                x = movingRight ? visibleCueRect.Right : visibleCueRect.Left;
            }
        }
        else
        {
            var movingRight = _movingColumnIndex >= 0 && _columnMoveCueIndex > _movingColumnIndex;
            x = movingRight ? visibleCueRect.Right : visibleCueRect.Left;
        }
        var insertionPen = new Pen(Brushes.Red, 2);
        dc.DrawLine(
            insertionPen,
            new Point(x, 1),
            new Point(x, Math.Max(1, ScaledColumnHeaderHeight - 1)));

        DrawDropArrows(dc, x, ScaledColumnHeaderHeight);
    }

    private void DrawRowMoveCue(DrawingContext dc)
    {
        if (!_isTrackingRowMove || !_isMovingRow)
        {
            return;
        }

        var clipRect = new Rect(0, ScaledColumnHeaderHeight, _rowHeaderWidth, _viewportBodyHeight);
        var currentPen = new Pen(Brushes.Red, 1);
        var movingRows = GetSelectedRowIndices();
        foreach (var movingRow in movingRows)
        {
            var movingRect = GetRowHeaderRect(movingRow);
            var visibleMovingRect = Rect.Intersect(movingRect, clipRect);
            if (visibleMovingRect.IsEmpty)
            {
                continue;
            }

            var currentRect = new Rect(
                visibleMovingRect.X + 0.5,
                visibleMovingRect.Y + 0.5,
                Math.Max(0, visibleMovingRect.Width - 1),
                Math.Max(0, visibleMovingRect.Height - 1));
            dc.DrawRectangle(null, currentPen, currentRect);
        }

        if (_rowMoveCueIndex < 0 || _rowMoveCueIndex >= Rows.Count)
        {
            return;
        }

        var cueRect = GetRowHeaderRect(_rowMoveCueIndex);
        if (cueRect.IsEmpty)
        {
            return;
        }

        if (!TryGetRowDropIndicatorY(out var y))
        {
            return;
        }

        y = Math.Clamp(y, clipRect.Top + 1, clipRect.Bottom - 1);
        var insertionPen = new Pen(Brushes.Red, 2);
        dc.DrawLine(
            insertionPen,
            new Point(1, y),
            new Point(Math.Max(1, _rowHeaderWidth - 1), y));

        DrawRowDropArrows(dc, y, _rowHeaderWidth, clipRect.Top + 1, clipRect.Bottom - 1);
    }

    private bool TryGetRowDropIndicatorY(out double y)
    {
        y = 0;
        if (_rowMoveCueIndex < 0 || _rowMoveCueIndex >= Rows.Count)
        {
            return false;
        }

        var selectedRows = GetSelectedRowIndices();
        if (selectedRows.Count == 0)
        {
            if (_movingRowIndex < 0 || _movingRowIndex >= Rows.Count || _rowMoveCueIndex == _movingRowIndex)
            {
                return false;
            }

            var cueRectSingle = GetRowHeaderRect(_rowMoveCueIndex);
            if (cueRectSingle.IsEmpty)
            {
                return false;
            }

            y = _rowMoveCueIndex > _movingRowIndex ? cueRectSingle.Bottom : cueRectSingle.Top;
            return true;
        }

        var minSelected = selectedRows[0];
        var maxSelected = selectedRows[^1];
        if (_rowMoveCueIndex >= minSelected && _rowMoveCueIndex <= maxSelected)
        {
            return false;
        }

        var cueRect = GetRowHeaderRect(_rowMoveCueIndex);
        if (cueRect.IsEmpty)
        {
            return false;
        }

        var insertAfterTarget = _rowMoveCueIndex > maxSelected;
        y = insertAfterTarget ? cueRect.Bottom : cueRect.Top;
        return true;
    }

    private static void DrawDropArrows(DrawingContext dc, double lineX, double headerHeight)
    {
        const double arrowWidth = 6;
        const double arrowHeight = 4;
        const double gap = 3;
        var centerY = headerHeight / 2.0;

        var red = Brushes.Red;

        // Left arrow pointing right.
        var leftArrow = new StreamGeometry();
        using (var ctx = leftArrow.Open())
        {
            var tip = new Point(lineX - gap, centerY);
            ctx.BeginFigure(new Point(tip.X - arrowWidth, tip.Y - arrowHeight), true, true);
            ctx.LineTo(new Point(tip.X - arrowWidth, tip.Y + arrowHeight), true, false);
            ctx.LineTo(tip, true, false);
        }
        leftArrow.Freeze();
        dc.DrawGeometry(red, null, leftArrow);

        // Right arrow pointing left.
        var rightArrow = new StreamGeometry();
        using (var ctx = rightArrow.Open())
        {
            var tip = new Point(lineX + gap, centerY);
            ctx.BeginFigure(new Point(tip.X + arrowWidth, tip.Y - arrowHeight), true, true);
            ctx.LineTo(new Point(tip.X + arrowWidth, tip.Y + arrowHeight), true, false);
            ctx.LineTo(tip, true, false);
        }
        rightArrow.Freeze();
        dc.DrawGeometry(red, null, rightArrow);
    }

    private static void DrawRowDropArrows(DrawingContext dc, double lineY, double headerWidth, double minY, double maxY)
    {
        const double arrowWidth = 4;
        const double arrowHeight = 6;
        const double gap = 3;
        var clampedLineY = Math.Clamp(lineY, minY, maxY);
        var centerX = headerWidth / 2.0;
        var red = Brushes.Red;

        // Down arrow above insertion line.
        var downArrow = new StreamGeometry();
        using (var ctx = downArrow.Open())
        {
            var tipY = Math.Clamp(clampedLineY - gap, minY + arrowHeight, maxY - arrowHeight);
            var tip = new Point(centerX, tipY);
            ctx.BeginFigure(new Point(tip.X - arrowWidth, tip.Y - arrowHeight), true, true);
            ctx.LineTo(new Point(tip.X + arrowWidth, tip.Y - arrowHeight), true, false);
            ctx.LineTo(tip, true, false);
        }
        downArrow.Freeze();
        dc.DrawGeometry(red, null, downArrow);

        // Up arrow below insertion line.
        var upArrow = new StreamGeometry();
        using (var ctx = upArrow.Open())
        {
            var tipY = Math.Clamp(clampedLineY + gap, minY + arrowHeight, maxY - arrowHeight);
            var tip = new Point(centerX, tipY);
            ctx.BeginFigure(new Point(tip.X - arrowWidth, tip.Y + arrowHeight), true, true);
            ctx.LineTo(new Point(tip.X + arrowWidth, tip.Y + arrowHeight), true, false);
            ctx.LineTo(tip, true, false);
        }
        upArrow.Freeze();
        dc.DrawGeometry(red, null, upArrow);
    }

    private Rect GetColumnHeaderRect(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= Columns.Count)
        {
            return Rect.Empty;
        }

        double left;
        if (columnIndex < _fixedColumnCount)
        {
            left = _rowHeaderWidth;
            for (var col = 0; col < columnIndex; col++)
            {
                left += GetColumnWidth(col);
            }
        }
        else
        {
            left = _rowHeaderWidth + GetFixedColumnsWidth();
            for (var col = _fixedColumnCount; col < columnIndex; col++)
            {
                left += GetColumnWidth(col);
            }

            left -= _horizontalOffset;
        }

        return new Rect(left, 0, GetColumnWidth(columnIndex), ScaledColumnHeaderHeight);
    }

    private Rect GetRowHeaderRect(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= Rows.Count)
        {
            return Rect.Empty;
        }

        var y = ScaledColumnHeaderHeight + GetRowTop(rowIndex) - _verticalOffset;
        return new Rect(0, y, _rowHeaderWidth, GetRowHeight(rowIndex));
    }

    private void SelectRow(int rowIndex, bool additive)
    {
        if (rowIndex < 0 || rowIndex >= Rows.Count)
        {
            return;
        }

        if (!additive)
        {
            _selectedCells.Clear();
        }

        for (var col = 0; col < Columns.Count; col++)
        {
            _selectedCells.Add(new GriddoCellAddress(rowIndex, col));
        }
    }

    private bool IsRowSelected(int rowIndex)
    {
        return _selectedCells.Any(c => c.RowIndex == rowIndex);
    }

    private bool IsColumnSelected(int columnIndex)
    {
        return _selectedCells.Any(c => c.ColumnIndex == columnIndex);
    }

    private List<int> GetSelectedColumnIndices()
    {
        return _selectedCells
            .Select(c => c.ColumnIndex)
            .Where(c => c >= 0 && c < Columns.Count)
            .Distinct()
            .OrderBy(c => c)
            .ToList();
    }

    private List<int> GetSelectedRowIndices()
    {
        return _selectedCells
            .Select(c => c.RowIndex)
            .Where(r => r >= 0 && r < Rows.Count)
            .Distinct()
            .OrderBy(r => r)
            .ToList();
    }

    private void ApplyRowHeaderDragSelection()
    {
        if (_rowHeaderDragAnchorRow < 0
            || _rowHeaderDragCurrentRow < 0
            || Rows.Count == 0
            || Columns.Count == 0)
        {
            return;
        }

        _selectedCells.Clear();
        if (_rowHeaderDragIsAdditive || (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _selectedCells.UnionWith(_selectionDragSnapshot);
        }

        var minRow = Math.Min(_rowHeaderDragAnchorRow, _rowHeaderDragCurrentRow);
        var maxRow = Math.Max(_rowHeaderDragAnchorRow, _rowHeaderDragCurrentRow);
        for (var row = minRow; row <= maxRow; row++)
        {
            for (var col = 0; col < Columns.Count; col++)
            {
                _selectedCells.Add(new GriddoCellAddress(row, col));
            }
        }
    }

    private void ApplyColumnHeaderDragSelection()
    {
        if (_columnHeaderDragAnchorColumn < 0
            || _columnHeaderDragCurrentColumn < 0
            || Rows.Count == 0
            || Columns.Count == 0)
        {
            return;
        }

        _selectedCells.Clear();
        if (_columnHeaderDragIsAdditive || (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _selectedCells.UnionWith(_selectionDragSnapshot);
        }

        var minCol = Math.Min(_columnHeaderDragAnchorColumn, _columnHeaderDragCurrentColumn);
        var maxCol = Math.Max(_columnHeaderDragAnchorColumn, _columnHeaderDragCurrentColumn);
        for (var col = minCol; col <= maxCol; col++)
        {
            for (var row = 0; row < Rows.Count; row++)
            {
                _selectedCells.Add(new GriddoCellAddress(row, col));
            }
        }
    }

    private void SelectProjectedColumnsFromCurrentRow(GriddoCellAddress current, int clickedColumn, bool additive)
    {
        if (Rows.Count == 0 || Columns.Count == 0)
        {
            return;
        }

        var row = Math.Clamp(current.RowIndex, 0, Rows.Count - 1);
        var currentCol = Math.Clamp(current.ColumnIndex, 0, Columns.Count - 1);
        var targetCol = Math.Clamp(clickedColumn, 0, Columns.Count - 1);

        if (!additive)
        {
            _selectedCells.Clear();
        }

        var selectedColumnsOnRow = _selectedCells
            .Where(c => c.RowIndex == row)
            .Select(c => c.ColumnIndex)
            .Distinct()
            .ToList();

        // If there is no explicit row selection yet, use current-to-clicked columns on the current row.
        if (selectedColumnsOnRow.Count == 0)
        {
            var minCol = Math.Min(currentCol, targetCol);
            var maxCol = Math.Max(currentCol, targetCol);
            for (var col = minCol; col <= maxCol; col++)
            {
                selectedColumnsOnRow.Add(col);
            }
        }

        if (!selectedColumnsOnRow.Contains(targetCol))
        {
            selectedColumnsOnRow.Add(targetCol);
        }

        foreach (var col in selectedColumnsOnRow)
        {
            if (col < 0 || col >= Columns.Count)
            {
                continue;
            }

            for (var r = 0; r < Rows.Count; r++)
            {
                _selectedCells.Add(new GriddoCellAddress(r, col));
            }
        }
    }

    private void IncludeRowsRangeForSelectedColumnsOnRow(int sourceRow, int targetRow)
    {
        if (Rows.Count == 0 || Columns.Count == 0)
        {
            return;
        }

        var fromRow = Math.Clamp(sourceRow, 0, Rows.Count - 1);
        var toRow = Math.Clamp(targetRow, 0, Rows.Count - 1);
        var minRow = Math.Min(fromRow, toRow);
        var maxRow = Math.Max(fromRow, toRow);

        var selectedColumnsOnRow = _selectedCells
            .Where(c => c.RowIndex == fromRow)
            .Select(c => c.ColumnIndex)
            .Distinct()
            .ToList();

        foreach (var col in selectedColumnsOnRow)
        {
            if (col < 0 || col >= Columns.Count)
            {
                continue;
            }

            for (var r = minRow; r <= maxRow; r++)
            {
                _selectedCells.Add(new GriddoCellAddress(r, col));
            }
        }
    }

    private void IncludeColumnsRangeForSelectedRowsOnColumn(int sourceColumn, int targetColumn)
    {
        if (Rows.Count == 0 || Columns.Count == 0)
        {
            return;
        }

        var fromCol = Math.Clamp(sourceColumn, 0, Columns.Count - 1);
        var toCol = Math.Clamp(targetColumn, 0, Columns.Count - 1);
        var minCol = Math.Min(fromCol, toCol);
        var maxCol = Math.Max(fromCol, toCol);

        var selectedRowsOnColumn = _selectedCells
            .Where(c => c.ColumnIndex == fromCol)
            .Select(c => c.RowIndex)
            .Distinct()
            .ToList();

        foreach (var row in selectedRowsOnColumn)
        {
            if (row < 0 || row >= Rows.Count)
            {
                continue;
            }

            for (var col = minCol; col <= maxCol; col++)
            {
                _selectedCells.Add(new GriddoCellAddress(row, col));
            }
        }
    }

    private GriddoCellAddress HitTestCell(Point point)
    {
        if (point.X < _rowHeaderWidth || point.Y < ScaledColumnHeaderHeight)
        {
            return new GriddoCellAddress(-1, -1);
        }

        if (point.X > _rowHeaderWidth + _viewportBodyWidth || point.Y > ScaledColumnHeaderHeight + _viewportBodyHeight)
        {
            return new GriddoCellAddress(-1, -1);
        }

        var row = HitTestRowIndex(point.Y - ScaledColumnHeaderHeight + _verticalOffset);
        if (row < 0)
        {
            return new GriddoCellAddress(-1, -1);
        }

        if (!TryMapViewportPointToContentX(point.X, out var contentX))
        {
            return new GriddoCellAddress(-1, -1);
        }

        var x = 0.0;
        for (var col = 0; col < Columns.Count; col++)
        {
            var width = GetColumnWidth(col);
            if (contentX >= x && contentX < x + width)
            {
                return new GriddoCellAddress(row, col);
            }

            x += width;
        }

        return new GriddoCellAddress(-1, -1);
    }

    private Rect GetCellRect(int row, int col)
    {
        if (row < 0 || row >= Rows.Count || col < 0 || col >= Columns.Count)
        {
            return Rect.Empty;
        }

        double x;
        if (col < _fixedColumnCount)
        {
            x = _rowHeaderWidth;
            for (var i = 0; i < col; i++)
            {
                x += GetColumnWidth(i);
            }
        }
        else
        {
            x = _rowHeaderWidth + GetFixedColumnsWidth();
            for (var i = _fixedColumnCount; i < col; i++)
            {
                x += GetColumnWidth(i);
            }

            x -= _horizontalOffset;
        }

        var y = GetRowTop(row);
        return new Rect(
            x,
            ScaledColumnHeaderHeight + y - _verticalOffset,
            GetColumnWidth(col),
            GetRowHeight(row));
    }

    private void CopySelectedCellsToClipboard()
    {
        if (_selectedCells.Count == 0)
        {
            return;
        }

        var minRow = _selectedCells.Min(c => c.RowIndex);
        var maxRow = _selectedCells.Max(c => c.RowIndex);
        var minCol = _selectedCells.Min(c => c.ColumnIndex);
        var maxCol = _selectedCells.Max(c => c.ColumnIndex);

        var lines = new List<string>();
        for (var row = minRow; row <= maxRow; row++)
        {
            var values = new List<string>();
            for (var col = minCol; col <= maxCol; col++)
            {
                var address = new GriddoCellAddress(row, col);
                if (!_selectedCells.Contains(address))
                {
                    values.Add(string.Empty);
                    continue;
                }

                var value = Columns[col].FormatValue(Columns[col].GetValue(Rows[row]));
                values.Add(value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '));
            }

            lines.Add(string.Join('\t', values));
        }

        var tableHtml = new StringBuilder();
        tableHtml.Append(
            "<table border=\"1\" cellspacing=\"0\" cellpadding=\"4\" style=\"border-collapse:collapse;font-family:Segoe UI,sans-serif;font-size:")
            .Append(EffectiveFontSize.ToString(CultureInfo.InvariantCulture))
            .Append("px\">");

        for (var row = minRow; row <= maxRow; row++)
        {
            tableHtml.Append("<tr>");
            for (var col = minCol; col <= maxCol; col++)
            {
                var address = new GriddoCellAddress(row, col);
                if (!_selectedCells.Contains(address))
                {
                    tableHtml.Append("<td></td>");
                    continue;
                }

                var column = Columns[col];
                var cellRect = GetCellRect(row, col);
                var cw = Math.Max(1, (int)Math.Round(cellRect.Width));
                var ch = Math.Max(1, (int)Math.Round(cellRect.Height));

                var flat = column.FormatValue(column.GetValue(Rows[row]))
                    .Replace('\t', ' ')
                    .Replace('\r', ' ')
                    .Replace('\n', ' ');

                if (column is IGriddoHostedColumnView hosted
                    && hosted.TryGetClipboardHtmlFragment(
                        TryGetHostedElement(address),
                        Rows[row],
                        cw,
                        ch,
                        out var fragment))
                {
                    tableHtml.Append("<td style=\"vertical-align:middle\">").Append(fragment).Append("</td>");
                }
                else if (column.IsHtml)
                {
                    var (pngBytes, pw, ph) = GriddoValuePainter.RenderHtmlCellToPng(
                        column.GetValue(Rows[row]),
                        cw,
                        ch,
                        column.ContentAlignment,
                        EffectiveFontSize);
                    var b64 = Convert.ToBase64String(pngBytes);
                    tableHtml.Append("<td style=\"vertical-align:middle\"><img src=\"data:image/png;base64,")
                        .Append(b64)
                        .Append("\" alt=\"\" width=\"")
                        .Append(pw)
                        .Append("\" height=\"")
                        .Append(ph)
                        .Append("\" /></td>");
                }
                else
                {
                    tableHtml.Append("<td style=\"vertical-align:middle\">")
                        .Append(GriddoClipboardHtml.EscapeCellText(flat))
                        .Append("</td>");
                }
            }

            tableHtml.Append("</tr>");
        }

        tableHtml.Append("</table>");

        var tsv = string.Join(Environment.NewLine, lines);
        var cfHtml = GriddoClipboardHtml.EncodeHtmlFragment(tableHtml.ToString());

        var dataObject = new DataObject();
        dataObject.SetText(tsv, TextDataFormat.UnicodeText);
        dataObject.SetText(tsv, TextDataFormat.Text);

        // CF_HTML offsets are UTF-8 byte counts; pass UTF-8 bytes so Excel gets valid HTML Format (string path can mis-encode).
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var htmlBytes = utf8.GetBytes(cfHtml);
        dataObject.SetData(DataFormats.Html, new MemoryStream(htmlBytes, writable: false));

        Clipboard.SetDataObject(dataObject, copy: true);
    }

    private void PasteClipboardIntoGrid()
    {
        if (!Clipboard.ContainsText())
        {
            return;
        }

        var startCell = _selectedCells.Count > 0
            ? new GriddoCellAddress(
                _selectedCells.Min(c => c.RowIndex),
                _selectedCells.Min(c => c.ColumnIndex))
            : _currentCell;
        if (startCell.RowIndex < 0 || startCell.ColumnIndex < 0)
        {
            return;
        }

        var text = Clipboard.GetText();
        var rowChunks = text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.None);

        for (var rowOffset = 0; rowOffset < rowChunks.Length; rowOffset++)
        {
            var targetRow = startCell.RowIndex + rowOffset;
            if (targetRow < 0 || targetRow >= Rows.Count)
            {
                break;
            }

            var cells = rowChunks[rowOffset].Split('\t');
            for (var colOffset = 0; colOffset < cells.Length; colOffset++)
            {
                var targetCol = startCell.ColumnIndex + colOffset;
                if (targetCol < 0 || targetCol >= Columns.Count)
                {
                    break;
                }

                var column = Columns[targetCol];
                if (column.Editor.TryCommit(cells[colOffset], out var parsed))
                {
                    column.TrySetValue(Rows[targetRow], parsed);
                }
            }
        }

        InvalidateVisual();
    }

    private double GetColumnWidth(int columnIndex)
    {
        var logical = _columnWidthOverrides.TryGetValue(columnIndex, out var o)
            ? o
            : Columns[columnIndex].Width;
        return Math.Max(MinColumnWidth, logical) * ContentScale;
    }

    private void SetColumnWidth(int columnIndex, double screenPixelWidth)
    {
        if (columnIndex < 0 || columnIndex >= Columns.Count)
        {
            return;
        }

        _columnWidthOverrides[columnIndex] = Math.Max(MinColumnWidth, screenPixelWidth / ContentScale);
        UpdateScrollBars();
    }

    private double GetRowHeight(int rowIndex)
    {
        _ = rowIndex;
        return Math.Max(MinRowHeight, _uniformRowHeight) * ContentScale;
    }

    private void SetUniformRowHeightFromScreen(double screenPixelHeight)
    {
        _uniformRowHeight = Math.Max(MinRowHeight, screenPixelHeight / ContentScale);
        UpdateScrollBars();
    }

    private void SetRowHeightKeepingRowTop(int rowIndex, double newScreenHeight)
    {
        if (Rows.Count == 0)
        {
            SetUniformRowHeightFromScreen(newScreenHeight);
            return;
        }

        var clampedRowIndex = Math.Clamp(rowIndex, 0, Rows.Count - 1);
        var oldHeight = GetRowHeight(clampedRowIndex);
        var oldOffset = _verticalOffset;

        SetUniformRowHeightFromScreen(newScreenHeight);

        var updatedHeight = GetRowHeight(clampedRowIndex);
        var offsetDelta = clampedRowIndex * (updatedHeight - oldHeight);
        SetVerticalOffset(oldOffset + offsetDelta);
    }

    private double GetRowTop(int rowIndex)
    {
        return rowIndex * GetRowHeight(0);
    }

    private int HitTestColumnDivider(Point point)
    {
        if (point.Y < 0 || point.Y > ScaledColumnHeaderHeight || point.X < _rowHeaderWidth || point.X > _rowHeaderWidth + _viewportBodyWidth)
        {
            return -1;
        }

        if (!TryMapViewportPointToContentX(point.X, out var contentX))
        {
            return -1;
        }

        var x = 0.0;
        for (var col = 0; col < Columns.Count; col++)
        {
            x += GetColumnWidth(col);
            if (Math.Abs(contentX - x) <= ScaledResizeGrip)
            {
                return col;
            }
        }

        return -1;
    }

    private bool HitTestTopLeftHeaderCell(Point point)
    {
        return point.X >= 0 &&
               point.X <= _rowHeaderWidth &&
               point.Y >= 0 &&
               point.Y <= ScaledColumnHeaderHeight;
    }

    private int HitTestColumnHeader(Point point)
    {
        if (point.Y < 0 || point.Y > ScaledColumnHeaderHeight || point.X < _rowHeaderWidth || point.X > _rowHeaderWidth + _viewportBodyWidth)
        {
            return -1;
        }

        if (!TryMapViewportPointToContentX(point.X, out var contentX))
        {
            return -1;
        }

        var x = 0.0;
        for (var col = 0; col < Columns.Count; col++)
        {
            var width = GetColumnWidth(col);
            if (contentX >= x && contentX < x + width)
            {
                return col;
            }

            x += width;
        }

        return -1;
    }

    private int HitTestRowDivider(Point point)
    {
        if (point.X < 0 || point.X > _rowHeaderWidth || point.Y < ScaledColumnHeaderHeight || point.Y > ScaledColumnHeaderHeight + _viewportBodyHeight)
        {
            return -1;
        }

        var y = 0.0;
        var contentY = point.Y - ScaledColumnHeaderHeight + _verticalOffset;
        for (var row = 0; row < Rows.Count; row++)
        {
            y += GetRowHeight(row);
            if (Math.Abs(contentY - y) <= ScaledResizeGrip)
            {
                return row;
            }
        }

        return -1;
    }

    private int HitTestRowHeader(Point point)
    {
        if (point.X < 0 || point.X > _rowHeaderWidth || point.Y < ScaledColumnHeaderHeight || point.Y > ScaledColumnHeaderHeight + _viewportBodyHeight)
        {
            return -1;
        }

        return HitTestRowIndex(point.Y - ScaledColumnHeaderHeight + _verticalOffset);
    }

    private int HitTestRowIndex(double pointerY)
    {
        if (Rows.Count == 0 || pointerY < 0)
        {
            return -1;
        }

        var rowHeight = GetRowHeight(0);
        var row = (int)(pointerY / rowHeight);
        return row >= 0 && row < Rows.Count ? row : -1;
    }

    private void UpdateResizeCursor(Point point)
    {
        if (HitTestColumnDivider(point) >= 0)
        {
            Cursor = Cursors.SizeWE;
            return;
        }

        if (HitTestRowDivider(point) >= 0)
        {
            Cursor = Cursors.SizeNS;
            return;
        }

        Cursor = Cursors.Arrow;
    }

    private void AutoSizeColumn(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= Columns.Count)
        {
            return;
        }

        var typeface = new Typeface("Segoe UI");
        var pad = 12 * _contentScale;
        var max = MeasureTextWidth(Columns[columnIndex].Header, typeface, EffectiveFontSize) + pad;
        for (var row = 0; row < Rows.Count; row++)
        {
            if (Columns[columnIndex] is IGriddoHostedColumnView)
            {
                continue;
            }

            var value = Columns[columnIndex].GetValue(Rows[row]);
            if (Columns[columnIndex].IsHtml || value is ImageSource or Geometry)
            {
                continue;
            }

            var text = Columns[columnIndex].FormatValue(value);
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            max = Math.Max(max, MeasureTextWidth(text, typeface, EffectiveFontSize) + pad);
        }

        SetColumnWidth(columnIndex, max);
        InvalidateVisual();
    }

    private void AutoSizeRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= Rows.Count)
        {
            return;
        }

        var typeface = new Typeface("Segoe UI");
        var pad = 6 * _contentScale;
        var max = MeasureTextHeight((rowIndex + 1).ToString(), typeface, EffectiveFontSize) + pad;
        for (var col = 0; col < Columns.Count; col++)
        {
            var value = Columns[col].GetValue(Rows[rowIndex]);
            max = Math.Max(max, MeasureCellHeight(value, typeface, EffectiveFontSize) + pad);
        }

        SetRowHeightKeepingRowTop(rowIndex, max);
        InvalidateVisual();
    }

    private void DrawScrollBarCorner(DrawingContext dc)
    {
        const double outerBorderInset = 1;
        var cornerThickness = Math.Max(0, ScrollBarSize - outerBorderInset);
        var topRightRect = new Rect(
            Math.Max(0, ActualWidth - ScrollBarSize - outerBorderInset),
            0,
            cornerThickness,
            Math.Max(0, ScaledColumnHeaderHeight));
        var bottomLeftRect = new Rect(
            0,
            Math.Max(0, ActualHeight - ScrollBarSize - outerBorderInset),
            Math.Max(0, _rowHeaderWidth),
            cornerThickness);
        var bottomRightRect = new Rect(
            Math.Max(0, ActualWidth - ScrollBarSize - outerBorderInset),
            Math.Max(0, ActualHeight - ScrollBarSize - outerBorderInset),
            cornerThickness,
            cornerThickness);

        dc.DrawRectangle(HeaderBackground, null, topRightRect);
        dc.DrawRectangle(HeaderBackground, null, bottomLeftRect);
        dc.DrawRectangle(HeaderBackground, null, bottomRightRect);

        var pen = new Pen(GridLineBrush, GridPenThickness);

        // Top-right: only top border.
        dc.DrawLine(
            pen,
            new Point(topRightRect.Left, topRightRect.Top),
            new Point(topRightRect.Right, topRightRect.Top));

        // Bottom-left: only left border.
        dc.DrawLine(
            pen,
            new Point(bottomLeftRect.Left, bottomLeftRect.Top),
            new Point(bottomLeftRect.Left, bottomLeftRect.Bottom));

        // Bottom-right: keep subtle separator to both scrollbars.
        dc.DrawLine(
            pen,
            new Point(bottomRightRect.Left, bottomRightRect.Top),
            new Point(bottomRightRect.Right, bottomRightRect.Top));
        dc.DrawLine(
            pen,
            new Point(bottomRightRect.Left, bottomRightRect.Top),
            new Point(bottomRightRect.Left, bottomRightRect.Bottom));
    }

    private void UpdateScrollBars()
    {
        var scrollViewport = GetScrollViewportWidth();
        var scrollContent = GetScrollableContentWidth();
        var contentHeight = GetContentHeight();
        var maxHorizontal = Math.Max(0, scrollContent - scrollViewport);
        var maxVertical = Math.Max(0, contentHeight - _viewportBodyHeight);

        _horizontalScrollBar.LargeChange = Math.Max(1, _viewportBodyWidth);
        _horizontalScrollBar.SmallChange = 16;
        _horizontalScrollBar.Maximum = maxHorizontal;

        _verticalScrollBar.LargeChange = Math.Max(1, _viewportBodyHeight);
        _verticalScrollBar.SmallChange = Math.Max(1, GetRowHeight(0));
        _verticalScrollBar.Maximum = maxVertical;

        SetHorizontalOffset(_horizontalOffset);
        SetVerticalOffset(_verticalOffset);
    }

    private double GetContentHeight()
    {
        return Rows.Count * GetRowHeight(0);
    }

    private void GetVisibleRowRange(out int startRow, out int endRow)
    {
        if (Rows.Count == 0 || _viewportBodyHeight <= 0)
        {
            startRow = 0;
            endRow = -1;
            return;
        }

        var rowHeight = GetRowHeight(0);
        startRow = Math.Clamp((int)(_verticalOffset / rowHeight), 0, Rows.Count - 1);
        endRow = Math.Clamp((int)Math.Ceiling((_verticalOffset + _viewportBodyHeight) / rowHeight) - 1, 0, Rows.Count - 1);
    }

    private void SetHorizontalOffset(double value)
    {
        var clamped = Math.Clamp(value, 0, _horizontalScrollBar.Maximum);
        if (Math.Abs(clamped - _horizontalOffset) < double.Epsilon)
        {
            return;
        }

        _horizontalOffset = clamped;
        if (Math.Abs(_horizontalScrollBar.Value - clamped) > double.Epsilon)
        {
            _horizontalScrollBar.Value = clamped;
        }

        InvalidateVisual();
    }

    private void SetVerticalOffset(double value)
    {
        var clamped = Math.Clamp(value, 0, _verticalScrollBar.Maximum);
        if (Math.Abs(clamped - _verticalOffset) < double.Epsilon)
        {
            return;
        }

        _verticalOffset = clamped;
        if (Math.Abs(_verticalScrollBar.Value - clamped) > double.Epsilon)
        {
            _verticalScrollBar.Value = clamped;
        }

        InvalidateVisual();
    }

    private void OnHorizontalScrollChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _horizontalOffset = e.NewValue;
        InvalidateVisual();
    }

    private void OnVerticalScrollChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _verticalOffset = e.NewValue;
        InvalidateVisual();
    }

    private void OnGridCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        _fixedColumnCount = Math.Clamp(_fixedColumnCount, 0, Math.Max(0, Columns.Count));
        UpdateRowHeaderWidth();
        UpdateScrollBars();
        UpdateHostCanvasClips();
        InvalidateVisual();
    }

    private void UpdateRowHeaderWidth()
    {
        var rowCountText = Math.Max(1, Rows.Count).ToString();
        var required = MeasureRowHeaderWidthForText(rowCountText);
        _rowHeaderWidth = Math.Max(MeasureRowHeaderWidthForText("1"), required);
    }

    private double MeasureRowHeaderWidthForText(string text)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            EffectiveFontSize,
            Brushes.Black,
            1.0);

        return Math.Ceiling(formatted.WidthIncludingTrailingWhitespace) + 14 * _contentScale;
    }

    private static double MeasureCellWidth(object? value, Typeface typeface, double fontSize)
    {
        return value switch
        {
            ImageSource image => image.Width,
            Geometry geometry => geometry.Bounds.Width,
            _ => MeasureTextWidth(value?.ToString() ?? string.Empty, typeface, fontSize)
        };
    }

    private static double MeasureCellHeight(object? value, Typeface typeface, double fontSize)
    {
        return value switch
        {
            ImageSource image => image.Height,
            Geometry geometry => geometry.Bounds.Height,
            _ => MeasureTextHeight(value?.ToString() ?? string.Empty, typeface, fontSize)
        };
    }

    private static double MeasureTextWidth(string text, Typeface typeface, double fontSize)
    {
        var formattedText = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            1.0);

        return formattedText.Width;
    }

    private static double MeasureTextHeight(string text, Typeface typeface, double fontSize)
    {
        var formattedText = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            1.0);

        return formattedText.Height;
    }
}
