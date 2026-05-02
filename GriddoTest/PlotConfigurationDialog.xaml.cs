using System.Globalization;
using System.Windows;
using Plotto.Charting.Controls;

namespace GriddoTest;

public partial class PlotConfigurationDialog
{
    private readonly SkiaChartBaseControl _chart;

    public PlotConfigurationDialog(SkiaChartBaseControl chart)
    {
        InitializeComponent();
        _chart = chart;
        Loaded += (_, _) => LoadFromChart();
    }

    private void LoadFromChart()
    {
        TitleText.Text = _chart.ChartTitle;
        LabelText.Text = _chart.ChartLabel;
        XAxisLabelText.Text = _chart.AxisLabelX;
        YAxisLabelText.Text = _chart.AxisLabelY;
        var v = _chart.Viewport;
        XMinText.Text = v.XMin.ToString(CultureInfo.CurrentCulture);
        XMaxText.Text = v.XMax.ToString(CultureInfo.CurrentCulture);
        YMinText.Text = v.YMin.ToString(CultureInfo.CurrentCulture);
        YMaxText.Text = v.YMax.ToString(CultureInfo.CurrentCulture);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(XMinText.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var xMin) ||
            !double.TryParse(XMaxText.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var xMax) ||
            !double.TryParse(YMinText.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var yMin) ||
            !double.TryParse(YMaxText.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var yMax))
        {
            MessageBox.Show(
                this,
                "Enter valid numbers for axis minimum and maximum.",
                "Plot configuration",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (xMax <= xMin || yMax <= yMin)
        {
            MessageBox.Show(
                this,
                "Each axis maximum must be greater than its minimum.",
                "Plot configuration",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _chart.ChartTitle = TitleText.Text;
        _chart.ChartLabel = LabelText.Text;
        _chart.AxisLabelX = XAxisLabelText.Text;
        _chart.AxisLabelY = YAxisLabelText.Text;
        _chart.Viewport.XMin = xMin;
        _chart.Viewport.XMax = xMax;
        _chart.Viewport.YMin = yMin;
        _chart.Viewport.YMax = yMax;
        _chart.Viewport.EnsureMinimumSize();
        _chart.InvalidateVisual();

        DialogResult = true;
        Close();
    }
}
