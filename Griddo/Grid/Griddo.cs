using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Griddo.Columns;
using Griddo.Editing;
using Griddo.Primitives;

namespace Griddo.Grid;

public sealed partial class Griddo : FrameworkElement
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
    private readonly System.Windows.Controls.Grid _scaleFeedbackLayer = new()
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
    private string _findText = string.Empty;
    private GriddoCellAddress _findMatchCell = new(-1, -1);
    private readonly HashSet<GriddoCellAddress> _findMatchedCells = [];
    private readonly List<string> _findHistory = [];
    private bool _pendingAutoFocus = true;
    private bool _hasAutoSizedColumns;
    private bool _initialSampleAutoSizeScheduled;
    private int _visibleRowCount;

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
        Loaded += OnLoadedRequestFocus;
        IsVisibleChanged += OnIsVisibleChangedRequestFocus;
    }

    private void OnLoadedRequestFocus(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        RequestAutoFocus();
    }

    private void OnIsVisibleChangedRequestFocus(object sender, DependencyPropertyChangedEventArgs e)
    {
        _ = sender;
        if (e.NewValue is true)
        {
            _pendingAutoFocus = true;
            RequestAutoFocus();
        }
    }

    private void RequestAutoFocus()
    {
        if (!_pendingAutoFocus || !IsVisible)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!_pendingAutoFocus || !IsVisible)
            {
                return;
            }

            Focus();
            Keyboard.Focus(this);
            if (IsKeyboardFocusWithin)
            {
                _pendingAutoFocus = false;
            }
        }));
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

    /// <summary>
    /// 0 (X) = use <see cref="UniformRowHeight"/>. 1..10 = fit exactly this many rows into visible body height.
    /// </summary>
    public int VisibleRowCount
    {
        get => _visibleRowCount;
        set
        {
            var clamped = Math.Clamp(value, 0, 10);
            if (_visibleRowCount == clamped)
            {
                return;
            }

            _visibleRowCount = clamped;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public Brush GridLineBrush { get; set; } = Brushes.LightGray;
    public Brush HeaderBackground { get; set; } = new SolidColorBrush(Color.FromRgb(245, 245, 245));
    public Brush SelectionBackground { get; set; } = new SolidColorBrush(Color.FromArgb(120, 102, 178, 255));
    public Brush CurrentCellBorderBrush { get; set; } = Brushes.DodgerBlue;
    public Brush FindMatchBackground { get; set; } = new SolidColorBrush(Color.FromArgb(170, 255, 235, 120));
    public bool HideCellSelectionColoring { get; set; }
    public bool HideHeaderSelectionColoring { get; set; }
    public bool HideCurrentCellColor { get; set; }
    public bool HideEditCellColor { get; set; }

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

            return stops[^1];
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

    private void OnGridCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        _fixedColumnCount = Math.Clamp(_fixedColumnCount, 0, Math.Max(0, Columns.Count));
        if (Rows.Count == 0)
        {
            _hasAutoSizedColumns = false;
            _initialSampleAutoSizeScheduled = false;
        }

        if (Rows.Count > 0 && Columns.Count > 0 && !_hasAutoSizedColumns && _columnWidthOverrides.Count == 0)
        {
            ScheduleInitialSampleAutoSize();
        }

        UpdateRowHeaderWidth();
        UpdateScrollBars();
        UpdateHostCanvasClips();
        InvalidateVisual();
    }

}
