using System.Windows;

namespace Griddo.Grid;

public sealed partial class Griddo
{
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
}
