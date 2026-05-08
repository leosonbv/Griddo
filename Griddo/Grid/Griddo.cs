using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Griddo.Fields;
using Griddo.Editing;
using Griddo.Primitives;

namespace Griddo.Grid;

public sealed partial class Griddo : FrameworkElement
{
    private static GriddoThemeKind _defaultTheme = GriddoThemeKind.Vs2013LightTheme;
    private static event Action<GriddoThemeKind>? DefaultThemeChanged;
    public static GriddoThemeKind DefaultTheme
    {
        get => _defaultTheme;
        set
        {
            if (_defaultTheme == value)
            {
                return;
            }

            _defaultTheme = value;
            DefaultThemeChanged?.Invoke(value);
        }
    }

    private enum HeaderFocusKind : byte
    {
        None,
        Corner,
        Field,
        Record,
    }

    private const double FieldHeaderHeightBase = 22;
    private const double DefaultRecordHeight = 24;
    private const double MinFieldWidth = 28;
    private const double MinRecordHeight = 18;
    private const double MinRecordTextPadding = 4;
    private const double ResizeGripSize = 4;
    private const double ScrollBarSize = 14;
    private const double ScrollBarButtonSizeScale = 2.0;
    private const double ScrollBarThumbMinSizeScale = 2.0;
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

    private static readonly double MinReadableRecordHeight = ComputeDefaultReadableRecordHeight();

    private readonly HashSet<GriddoCellAddress> _selectedCells = [];
    /// <summary>Field indices whose headers stay highlighted after a field-header context gesture cleared body cells.</summary>
    private readonly HashSet<int> _fieldHeaderOnlySelection = [];

    /// <summary>Record indices whose headers stay highlighted after a record-header context gesture cleared body cells.</summary>
    private readonly HashSet<int> _recordHeaderOnlySelection = [];

    /// <summary>Field headers outlined in red after a field-header right-click (context scope).</summary>
    private readonly HashSet<int> _fieldHeaderRightClickOutline = [];

    /// <summary>Record headers outlined in red after a record-header right-click (context scope).</summary>
    private readonly HashSet<int> _recordHeaderRightClickOutline = [];
    private readonly HashSet<GriddoCellAddress> _selectionDragSnapshot = [];
    private readonly Dictionary<IGriddoFieldView, double> _fieldWidthOverrides = [];
    /// <summary>Grid field indices that should not receive initial sample auto-width (e.g. width restored from persistence).</summary>
    private readonly HashSet<IGriddoFieldView> _suppressInitialAutoWidthFields = [];
    private readonly GriddoTextEditSession _editSession = new();
    private ContextMenu? _activeEditOptionsMenu;
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
    private readonly List<GriddoSortDescriptor> _sortDescriptors = [];
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
    private double _uniformRecordHeight = DefaultRecordHeight;
    private GriddoCellAddress _currentCell = new(0, 0);
    private bool _isEditing;
    private bool _isCommittingEdit;
    private bool _hasKeyboardSelectionAnchor;
    private GriddoCellAddress _keyboardSelectionAnchor;
    private bool _isDraggingSelection;
    private bool _dragIsAdditive;
    private GriddoCellAddress _dragAnchorCell;
    private GriddoCellAddress _dragCurrentCell;
    private bool _pendingHostedEditActivation;
    private GriddoCellAddress _pendingHostedEditCell;
    private bool _isDraggingEditSelection;
    private bool _isResizingField;
    private bool _isResizingRecord;
    private int _resizingFieldIndex = -1;
    private int _resizingRecordIndex = -1;
    private Point _resizeStartPoint;
    private double _resizeInitialSize;
    private double _resizePreserveOldRecordHeight;
    private double _resizePreserveOldVerticalOffset;
    private double _resizePreserveOldHorizontalOffset;
    private bool _isTrackingFieldMove;
    private bool _isMovingField;
    private bool _isMovingPointerInFieldHeader;
    private int _movingFieldIndex = -1;
    private int _fieldMoveCueIndex = -1;
    private Point _fieldMoveStartPoint;
    private bool _fieldMoveStartedFromSelectedHeader;
    private bool _pendingFieldHeaderSelectionOnMouseUp;
    private int _pendingFieldHeaderIndex = -1;
    private bool _pendingFieldHeaderSelectionAdditive;
    private bool _pendingFieldHeaderPreserveSelection;
    private bool _isDraggingFieldHeaderSelection;
    private bool _fieldHeaderDragIsAdditive;
    private int _fieldHeaderDragAnchorField = -1;
    private int _fieldHeaderDragCurrentField = -1;
    private bool _isTrackingRecordMove;
    private bool _isMovingRecord;
    private int _movingRecordIndex = -1;
    private int _recordMoveCueIndex = -1;
    private Point _recordMoveStartPoint;
    private bool _pendingRecordHeaderSelectionOnMouseUp;
    private int _pendingRecordHeaderIndex = -1;
    private bool _pendingRecordHeaderSelectionAdditive;
    private bool _pendingRecordHeaderPreserveSelection;
    private bool _isDraggingRecordHeaderSelection;
    private bool _recordHeaderDragIsAdditive;
    private int _recordHeaderDragAnchorRecord = -1;
    private int _recordHeaderDragCurrentRecord = -1;
    private HeaderFocusKind _headerFocusKind;
    private int _headerFocusFieldIndex;
    private int _headerFocusRecordIndex;
    private double _horizontalOffset;
    private int _fixedFieldCount;
    private int _fixedRecordCount;
    private double _verticalOffset;
    private double _viewportBodyWidth;
    private double _viewportBodyHeight;
    private double _recordHeaderWidth = 40;
    private string _findText = string.Empty;
    private GriddoCellAddress _findMatchCell = new(-1, -1);
    private readonly HashSet<GriddoCellAddress> _findMatchedCells = [];
    private readonly List<string> _findHistory = [];
    private bool _pendingAutoFocus = true;
    private bool _hasAutoSizedFields;
    private bool _initialSampleAutoSizeScheduled;
    private int _visibleRecordCount;
    private int _suspendGridCollectionChanged;
    private readonly ToolTip _fieldHeaderToolTip = new();
    private bool _fieldHeaderToolTipNeedsReattach;
    private bool _priorPointerOnDescribedFieldHeader;
    private int _fieldHeaderToolTipClosedSuppress;

    /// <summary>
    /// Synthetic MouseDown from hosted-plot direct-edit relay bubbles to this element; when non-zero, ignore that re-entrant pass.
    /// </summary>
    private int _hostedDirectRelayDepth;

    public Griddo()
    {
        Focusable = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Records = new ObservableCollection<object>();
        Fields = new ObservableCollection<IGriddoFieldView>();
        _children = new VisualCollection(this);

        _horizontalScrollBar = new ScrollBar
        {
            Orientation = Orientation.Horizontal,
            Minimum = 0
        };
        _horizontalScrollBar.Resources[SystemParameters.HorizontalScrollBarButtonWidthKey] =
            SystemParameters.HorizontalScrollBarButtonWidth * ScrollBarButtonSizeScale;
        _horizontalScrollBar.Resources[SystemParameters.HorizontalScrollBarThumbWidthKey] =
            SystemParameters.HorizontalScrollBarThumbWidth * ScrollBarThumbMinSizeScale;
        _horizontalScrollBar.ValueChanged += OnHorizontalScrollChanged;

        _children.Add(_scrollHostCanvas);

        _verticalScrollBar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Minimum = 0
        };
        _verticalScrollBar.Resources[SystemParameters.VerticalScrollBarButtonHeightKey] =
            SystemParameters.VerticalScrollBarButtonHeight * ScrollBarButtonSizeScale;
        _verticalScrollBar.Resources[SystemParameters.VerticalScrollBarThumbHeightKey] =
            SystemParameters.VerticalScrollBarThumbHeight * ScrollBarThumbMinSizeScale;
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
        CellContextMenu = BuildDefaultBodyCellContextMenu();

        Records.CollectionChanged += OnGridCollectionChanged;
        Fields.CollectionChanged += OnGridCollectionChanged;
        UpdateRecordHeaderWidth();
        _fieldHeaderToolTip.HasDropShadow = false;
        _fieldHeaderToolTip.BorderThickness = new Thickness(0);
        _fieldHeaderToolTip.Closed += FieldHeaderToolTipOnClosed;
        ToolTip = _fieldHeaderToolTip;
        ToolTipService.SetBetweenShowDelay(this, 0);
        Loaded += OnLoadedThemeRegistration;
        Unloaded += OnUnloadedThemeRegistration;
        Loaded += OnLoadedRequestFocus;
        IsVisibleChanged += OnIsVisibleChangedRequestFocus;
        ApplyTheme(_theme);
    }

    private void OnLoadedThemeRegistration(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        DefaultThemeChanged -= HandleDefaultThemeChanged;
        DefaultThemeChanged += HandleDefaultThemeChanged;
        if (_theme != DefaultTheme)
        {
            ApplyTheme(DefaultTheme);
        }
    }

    private void OnUnloadedThemeRegistration(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        DefaultThemeChanged -= HandleDefaultThemeChanged;
    }

    private void HandleDefaultThemeChanged(GriddoThemeKind theme)
    {
        if (_theme == theme)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() => ApplyTheme(theme)));
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

    public ObservableCollection<object> Records { get; }
    public ObservableCollection<IGriddoFieldView> Fields { get; }

    public IReadOnlyCollection<GriddoCellAddress> SelectedCells => _selectedCells;

    /// <summary>Keyboard/mouse focus cell (not necessarily the only selected cell when a range is selected).</summary>
    public GriddoCellAddress CurrentCell => _currentCell;

    public event EventHandler<GriddoFieldHeaderMouseEventArgs>? FieldHeaderRightClick;
    public event EventHandler? SortDescriptorsChanged;
    public event EventHandler? UniformRecordHeightChanged;
    /// <summary>Fires when <see cref="ContentScale"/> changes (e.g. Ctrl+mouse wheel).</summary>
    public event EventHandler? ContentScaleChanged;

    /// <summary>Fires when column/field widths change (divider drag, double-click auto-fit, <see cref="AutoSizeAllFields"/>).</summary>
    public event EventHandler? FieldWidthsChanged;

    /// <summary>Fires on record header right-click; see <see cref="GriddoRecordHeaderMouseEventArgs.SelectedRecordIndices"/> for the full scope.</summary>
    public event EventHandler<GriddoRecordHeaderMouseEventArgs>? RecordHeaderRightClick;
    /// <summary>Fires on right-click in the top-left corner header cell.</summary>
    public event EventHandler? CornerHeaderRightClick;
    public IReadOnlyList<GriddoSortDescriptor> SortDescriptors => _sortDescriptors;

    /// <summary>Optional context menu for body-cell right-click (after selection rules are applied).</summary>
    public ContextMenu? CellContextMenu { get; set; }
    public Func<object, int, GriddoCellPropertyView?>? CellPropertyViewResolver { get; set; }

    /// <summary>Fires before <see cref="CellContextMenu"/> opens; set <see cref="GriddoCellContextMenuEventArgs.Handled"/> to suppress the default menu.</summary>
    public event EventHandler<GriddoCellContextMenuEventArgs>? CellContextMenuOpening;

    /// <summary>Uniform record height for all records (minimum applies).</summary>
    public double UniformRecordHeight
    {
        get => _uniformRecordHeight;
        set
        {
            var clamped = Math.Max(GetMinimumRecordThickness(), value);
            if (Math.Abs(_uniformRecordHeight - clamped) < double.Epsilon)
            {
                return;
            }

            _uniformRecordHeight = clamped;
            UniformRecordHeightChanged?.Invoke(this, EventArgs.Empty);
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public static double GetDefaultMinimumRecordThickness() => MinReadableRecordHeight;

    private static double ComputeDefaultReadableRecordHeight()
    {
        var typeface = new Typeface("Segoe UI");
        var probe = new FormattedText(
            "Ag",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            12.0,
            Brushes.Black,
            1.0);
        return Math.Max(MinRecordHeight, Math.Ceiling(probe.Height + MinRecordTextPadding));
    }

    private static double GetMinimumRecordThickness()
    {
        return MinReadableRecordHeight;
    }

    /// <summary>
    /// 0 = use <see cref="UniformRecordHeight"/>. 1..10 = divide visible body height into that many row slots.
    /// When <see cref="Records"/> has fewer rows than this value, each row grows so the existing rows fill the viewport.
    /// </summary>
    public int VisibleRecordCount
    {
        get => _visibleRecordCount;
        set
        {
            var clamped = Math.Clamp(value, 0, 10);
            if (_visibleRecordCount == clamped)
            {
                return;
            }

            _visibleRecordCount = clamped;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private bool _isTransposed;

    /// <summary>
    /// When true, logical records extend horizontally and logical fields vertically (record headers on top, field headers on the left).
    /// Horizontal scroll follows records; vertical scroll follows fields.
    /// </summary>
    public bool IsTransposed
    {
        get => _isTransposed;
        set
        {
            if (_isTransposed == value)
            {
                return;
            }

            _isTransposed = value;
            UpdateScrollBars();
            InvalidateMeasure();
            InvalidateVisual();
            RefreshHostedCells();
        }
    }

    public Brush GridLineBrush { get; set; } = Brushes.LightGray;
    public Brush HeaderBackground { get; set; } = new SolidColorBrush(Color.FromRgb(245, 245, 245));
    public Brush HeaderForeground { get; set; } = Brushes.Black;
    public Brush HeaderSelectionForeground { get; set; } = Brushes.White;
    public FontWeight HeaderFontWeight { get; set; } = FontWeights.Bold;
    public Brush BodyBackground { get; set; } = Brushes.White;
    public Brush BodyForeground { get; set; } = Brushes.Black;
    public Brush SelectionBackground { get; set; } = new SolidColorBrush(Color.FromArgb(120, 102, 178, 255));
    public Brush HeaderSelectionBackground { get; set; } = new SolidColorBrush(Color.FromArgb(160, 102, 178, 255));
    public Brush CurrentCellBorderBrush { get; set; } = Brushes.DodgerBlue;
    public Brush FindMatchBackground { get; set; } = new SolidColorBrush(Color.FromArgb(170, 255, 235, 120));
    private bool _showFieldHeaderSelectionColoring = true;
    private bool _showRecordHeaderSelectionColoring = true;
    private bool _showHorizontalScrollBar = true;
    private bool _showVerticalScrollBar = true;
    private bool _immediateCellEditOnSingleClick;
    private bool _hideSelectionWhenGridLosesFocus;
    private GriddoThemeKind _theme = DefaultTheme;
    public bool ShowCellSelectionColoring { get; set; } = true;
    public bool HideSelectionWhenGridLosesFocus
    {
        get => _hideSelectionWhenGridLosesFocus;
        set
        {
            if (_hideSelectionWhenGridLosesFocus == value)
            {
                return;
            }

            _hideSelectionWhenGridLosesFocus = value;
            InvalidateVisual();
        }
    }
    public bool ShowSortingIndicators { get; set; } = true;
    public GriddoThemeKind Theme
    {
        get => _theme;
        set
        {
            if (_theme == value)
            {
                return;
            }

            ApplyTheme(value);
        }
    }

    public bool ShowHeaderSelectionColoring
    {
        get => ShowFieldHeaderSelectionColoring && ShowRecordHeaderSelectionColoring;
        set
        {
            ShowFieldHeaderSelectionColoring = value;
            ShowRecordHeaderSelectionColoring = value;
        }
    }
    public bool ShowFieldHeaderSelectionColoring
    {
        get => _showFieldHeaderSelectionColoring;
        set
        {
            if (_showFieldHeaderSelectionColoring == value)
            {
                return;
            }

            _showFieldHeaderSelectionColoring = value;
            InvalidateVisual();
        }
    }
    public bool ShowRecordHeaderSelectionColoring
    {
        get => _showRecordHeaderSelectionColoring;
        set
        {
            if (_showRecordHeaderSelectionColoring == value)
            {
                return;
            }

            _showRecordHeaderSelectionColoring = value;
            InvalidateVisual();
        }
    }
    public bool ShowCurrentCellColor { get; set; } = true;
    public bool ShowEditCellColor { get; set; } = true;
    public bool ShowHorizontalScrollBar
    {
        get => _showHorizontalScrollBar;
        set
        {
            if (_showHorizontalScrollBar == value)
            {
                return;
            }

            _showHorizontalScrollBar = value;
            _horizontalScrollBar.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            InvalidateMeasure();
            UpdateScrollBars();
            InvalidateVisual();
        }
    }
    public bool ShowVerticalScrollBar
    {
        get => _showVerticalScrollBar;
        set
        {
            if (_showVerticalScrollBar == value)
            {
                return;
            }

            _showVerticalScrollBar = value;
            _verticalScrollBar.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            InvalidateMeasure();
            UpdateScrollBars();
            InvalidateVisual();
        }
    }
    public bool ImmediateCellEditOnSingleClick
    {
        get => _immediateCellEditOnSingleClick;
        set => _immediateCellEditOnSingleClick = value;
    }

    public void ApplyTheme(GriddoThemeKind theme)
    {
        _theme = theme;
        switch (theme)
        {
            case GriddoThemeKind.Vs2013DarkTheme:
                GridLineBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66));
                HeaderBackground = new SolidColorBrush(Color.FromRgb(45, 45, 48));
                HeaderForeground = new SolidColorBrush(Color.FromRgb(241, 241, 241));
                HeaderSelectionForeground = Brushes.White;
                HeaderFontWeight = FontWeights.Normal;
                BodyBackground = new SolidColorBrush(Color.FromRgb(37, 37, 38));
                BodyForeground = new SolidColorBrush(Color.FromRgb(241, 241, 241));
                SelectionBackground = new SolidColorBrush(Color.FromArgb(95, 0, 122, 204));
                HeaderSelectionBackground = new SolidColorBrush(Color.FromRgb(0, 122, 204));
                CurrentCellBorderBrush = new SolidColorBrush(Color.FromRgb(160, 210, 255));
                FindMatchBackground = new SolidColorBrush(Color.FromArgb(170, 180, 160, 70));
                break;
            case GriddoThemeKind.Vs2013LightTheme:
            default:
                GridLineBrush = new SolidColorBrush(Color.FromRgb(217, 217, 217));
                HeaderBackground = new SolidColorBrush(Color.FromRgb(240, 240, 240));
                HeaderForeground = Brushes.Black;
                HeaderSelectionForeground = Brushes.White;
                HeaderFontWeight = FontWeights.Normal;
                BodyBackground = Brushes.White;
                BodyForeground = Brushes.Black;
                SelectionBackground = new SolidColorBrush(Color.FromArgb(120, 102, 178, 255));
                HeaderSelectionBackground = new SolidColorBrush(Color.FromRgb(0, 122, 204));
                CurrentCellBorderBrush = Brushes.DodgerBlue;
                FindMatchBackground = new SolidColorBrush(Color.FromArgb(170, 255, 235, 120));
                break;
        }

        _horizontalScrollBar.Background = HeaderBackground;
        _horizontalScrollBar.Foreground = HeaderForeground;
        _verticalScrollBar.Background = HeaderBackground;
        _verticalScrollBar.Foreground = HeaderForeground;

        InvalidateVisual();
    }

    /// <summary>Pen stroke for the right edge of the last fixed field only (freeze boundary before scrollable fields).</summary>
    public Brush FixedFieldRightBorderBrush { get; set; } = new SolidColorBrush(Color.FromRgb(118, 118, 118));

    /// <summary>Pen stroke for the bottom edge of the last fixed record only (freeze boundary above scrollable records).</summary>
    public Brush FixedRecordBottomBorderBrush { get; set; } = new SolidColorBrush(Color.FromRgb(118, 118, 118));

    /// <summary>Number of leading fields that remain fixed on the left when scrolling horizontally (0 = off).</summary>
    public int FixedFieldCount
    {
        get => _fixedFieldCount;
        set
        {
            var v = Math.Clamp(value, 0, Math.Max(0, Fields.Count));
            if (v == _fixedFieldCount)
            {
                return;
            }

            _fixedFieldCount = v;
            UpdateScrollBars();
            UpdateHostCanvasClips();
            InvalidateVisual();
        }
    }

    /// <summary>Number of leading records that stay fixed at the top when scrolling vertically (0 = off).</summary>
    public int FixedRecordCount
    {
        get => _fixedRecordCount;
        set
        {
            var v = Math.Clamp(value, 0, Math.Max(0, Records.Count));
            if (v == _fixedRecordCount)
            {
                return;
            }

            _fixedRecordCount = v;
            UpdateScrollBars();
            InvalidateVisual();
        }
    }

    private double _contentScale = 1.0;
    private bool _hostedPlotDirectEditOnMouseDown;

    /// <summary>
    /// When true, a single left click on a hosted Plot field activates chart edit mode on mouse down and forwards that press to the chart.
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

    /// <summary>Ctrl+mouse wheel: scales record/field sizes, cell fonts, grid lines, and hosted Plotto stroke widths.</summary>
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
            UpdateRecordHeaderWidth();
            InvalidateMeasure();
            InvalidateVisual();
            ContentScaleChanged?.Invoke(this, EventArgs.Empty);
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

    private double ScaledFieldHeaderHeight => FieldHeaderHeightBase * _contentScale;

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
        if (_suspendGridCollectionChanged > 0)
        {
            return;
        }

        _fixedFieldCount = Math.Clamp(_fixedFieldCount, 0, Math.Max(0, Fields.Count));
        _fixedRecordCount = Math.Clamp(_fixedRecordCount, 0, Math.Max(0, Records.Count));
        if (Records.Count == 0)
        {
            _hasAutoSizedFields = false;
            _initialSampleAutoSizeScheduled = false;
            _suppressInitialAutoWidthFields.Clear();
        }

        if (Records.Count > 0 && Fields.Count > 0 && !_hasAutoSizedFields)
        {
            ScheduleInitialSampleAutoSize();
        }

        UpdateRecordHeaderWidth();
        UpdateScrollBars();
        UpdateHostCanvasClips();
        InvalidateVisual();

        // Keep rows ordered when sort keys are active (host bulk-add / Records.Add does not call SetSortDescriptors).
        if (_sortDescriptors.Count > 0 && Records.Count > 1 && Fields.Count > 0)
        {
            ApplySorting();
        }
    }

}
