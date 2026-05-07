using System.Windows;
using System.Windows.Controls;
using Plotto.Charting.Core;

namespace GriddoTest.Stability;

public sealed class StabilityExamplesWindow : Window
{
    public StabilityExamplesWindow()
    {
        Title = "Stability plot examples";
        Width = 1120;
        Height = 820;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { LastChildFill = true, Margin = new Thickness(10) };
        Content = root;

        var hint = new TextBlock
        {
            Text = "Mouse wheel zoom enabled in editor mode. Use buttons for deterministic X/Y zoom steps.",
            Margin = new Thickness(4, 0, 4, 8)
        };
        DockPanel.SetDock(hint, Dock.Top);
        root.Children.Add(hint);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(grid);

        AddStabilityRow(
            grid,
            row: 0,
            title: "Intensity vs acquisition time",
            yAxis: "Intensity",
            mean: 1000,
            sigma: 55,
            points: BuildSeries(60, t => 1000 + 40 * Math.Sin(t / 5.0) + 28 * Math.Cos(t / 11.0)));

        AddStabilityRow(
            grid,
            row: 1,
            title: "Concentration vs acquisition time",
            yAxis: "Concentration",
            mean: 12.5,
            sigma: 0.7,
            points: BuildSeries(60, t => 12.5 + 0.55 * Math.Sin(t / 6.0) + 0.3 * Math.Cos(t / 13.0)));

        AddStabilityRow(
            grid,
            row: 2,
            title: "Retention time vs acquisition time",
            yAxis: "Retention time",
            mean: 4.85,
            sigma: 0.08,
            points: BuildSeries(60, t => 4.85 + 0.07 * Math.Sin(t / 7.0) + 0.03 * Math.Cos(t / 4.0)));
    }

    private static List<ChartPoint> BuildSeries(int count, Func<int, double> ySelector)
    {
        var points = new List<ChartPoint>(count);
        for (var i = 0; i < count; i++)
        {
            points.Add(new ChartPoint(i, ySelector(i)));
        }

        return points;
    }

    private static void AddStabilityRow(
        Grid grid,
        int row,
        string title,
        string yAxis,
        double mean,
        double sigma,
        IReadOnlyList<ChartPoint> points)
    {
        var panel = new DockPanel { Margin = new Thickness(0, row == 0 ? 0 : 10, 0, 0) };
        Grid.SetRow(panel, row);
        grid.Children.Add(panel);

        var chart = new StabilityPlotControl
        {
            RequireActivationClick = false,
            RenderMode = ChartRenderMode.Editor,
            EnableInlineEditing = true,
            EnableMouseInteractions = true,
            ShowChartTitle = true,
            ChartTitle = title,
            AxisLabelX = "Acquisition time",
            AxisLabelY = yAxis,
            MeanValue = mean,
            StandardDeviation = sigma,
            SigmaMultipliers = [1, 2, 3],
            Points = points
        };

        var buttonBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 6)
        };
        DockPanel.SetDock(buttonBar, Dock.Top);
        panel.Children.Add(buttonBar);

        void AddButton(string text, Action onClick)
        {
            var btn = new Button
            {
                Content = text,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(8, 2, 8, 2),
                MinWidth = 64
            };
            btn.Click += (_, _) => onClick();
            buttonBar.Children.Add(btn);
        }

        AddButton("Zoom X+", chart.ZoomInX);
        AddButton("Zoom X-", chart.ZoomOutX);
        AddButton("Zoom Y+", chart.ZoomInY);
        AddButton("Zoom Y-", chart.ZoomOutY);
        AddButton("Reset", chart.ZoomOutCompletely);

        panel.Children.Add(chart);
    }
}
