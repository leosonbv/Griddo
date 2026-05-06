using System.Globalization;
using System.Windows;

namespace GriddoTest;

public partial class PlotConfigurationDialog
{
    private readonly IPlotFieldLayoutTarget _initial;
    private readonly Action<PlotFieldDialogResult>? _previewApply;
    public PlotFieldDialogResult? Result { get; private set; }

    public PlotConfigurationDialog(IPlotFieldLayoutTarget initial, Action<PlotFieldDialogResult>? previewApply = null)
    {
        InitializeComponent();
        _initial = initial;
        _previewApply = previewApply;
        Loaded += (_, _) => LoadFromChart();
    }

    private void LoadFromChart()
    {
        TitleText.Text = _initial.TitleSelection;
        LabelText.Text = _initial.Label;
        ShowXAxisCheck.IsChecked = _initial.ShowXAxis;
        ShowYAxisCheck.IsChecked = _initial.ShowYAxis;
        XAxisLabelText.Text = _initial.XAxisTitle;
        YAxisLabelText.Text = _initial.YAxisTitle;
        XAxisUnitText.Text = _initial.XAxisUnit;
        YAxisUnitText.Text = _initial.YAxisUnit;
        XAxisPrecisionText.Text = _initial.XAxisLabelPrecision.ToString(CultureInfo.CurrentCulture);
        YAxisPrecisionText.Text = _initial.YAxisLabelPrecision.ToString(CultureInfo.CurrentCulture);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildResult(out var result))
        {
            return;
        }

        Result = result;

        DialogResult = true;
        Close();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        if (!TryBuildResult(out var result))
        {
            return;
        }

        Result = result;
        _previewApply?.Invoke(result);
    }

    private bool TryBuildResult(out PlotFieldDialogResult result)
    {
        result = default!;
        if (!int.TryParse(XAxisPrecisionText.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var xPrecision) ||
            !int.TryParse(YAxisPrecisionText.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var yPrecision))
        {
            MessageBox.Show(
                this,
                "Enter valid integer values for axis precision.",
                "Plot configuration",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        result = new PlotFieldDialogResult(
            TitleSelection: TitleText.Text ?? string.Empty,
            Label: LabelText.Text ?? string.Empty,
            ShowXAxis: ShowXAxisCheck.IsChecked == true,
            ShowYAxis: ShowYAxisCheck.IsChecked == true,
            XAxisTitle: XAxisLabelText.Text ?? string.Empty,
            YAxisTitle: YAxisLabelText.Text ?? string.Empty,
            XAxisUnit: XAxisUnitText.Text ?? string.Empty,
            YAxisUnit: YAxisUnitText.Text ?? string.Empty,
            XAxisLabelPrecision: Math.Clamp(xPrecision, 0, 10),
            YAxisLabelPrecision: Math.Clamp(yPrecision, 0, 10));
        return true;
    }
}

public sealed record PlotFieldDialogResult(
    string TitleSelection,
    string Label,
    bool ShowXAxis,
    bool ShowYAxis,
    string XAxisTitle,
    string YAxisTitle,
    string XAxisUnit,
    string YAxisUnit,
    int XAxisLabelPrecision,
    int YAxisLabelPrecision);
