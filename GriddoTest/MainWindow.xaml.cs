using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Griddo;
using SkiaSharp;
using Plotto.Charting.Controls;
using Plotto.Charting.Core;
using System.Collections.Generic;

namespace GriddoTest
{
    public partial class MainWindow : Window
    {
        private const string PlottoColumnHeader = "Plotto Cell";
        private const string CalibrationColumnHeader = "Calibration";

        private readonly ChromatogramControl _plotConfigFallback = new()
        {
            AxisLabelX = "Time",
            AxisLabelY = "Intensity",
            ChartTitle = "Chromatogram"
        };

        public MainWindow()
        {
            InitializeComponent();
            ConfigureGrid();
            DemoGrid.ColumnHeaderRightClick += DemoGrid_ColumnHeaderRightClick;
            DemoGrid.UniformRowHeight = 132;
        }

        private void DemoGrid_ColumnHeaderRightClick(object? sender, GriddoColumnHeaderMouseEventArgs e)
        {
            if (e.ColumnIndex < 0 || e.ColumnIndex >= DemoGrid.Columns.Count)
            {
                return;
            }

            if (!string.Equals(DemoGrid.Columns[e.ColumnIndex].Header, PlottoColumnHeader, StringComparison.Ordinal))
            {
                return;
            }

            var dlg = new PlotConfigurationDialog(ResolvePlotConfigurationChart()) { Owner = this };
            dlg.ShowDialog();
        }

        private ChromatogramControl ResolvePlotConfigurationChart()
        {
            var cell = DemoGrid.SelectedCells.FirstOrDefault(c => c.IsValid);
            if (cell.IsValid && DemoGrid.TryGetHostedElement(cell) is Border { Child: ChromatogramControl chart })
            {
                return chart;
            }

            return _plotConfigFallback;
        }

        private void ConfigureGrid()
        {
            DemoGrid.Columns.Add(new GriddoColumnView(
                header: "ID",
                width: 70,
                valueGetter: row => ((DemoRow)row).Id,
                valueSetter: (row, value) =>
                {
                    var text = value?.ToString();
                    if (!int.TryParse(text, out var id))
                    {
                        return false;
                    }

                    ((DemoRow)row).Id = id;
                    return true;
                },
                editor: GriddoCellEditors.Number));

            DemoGrid.Columns.Add(new GriddoColumnView(
                header: "Name",
                width: 180,
                valueGetter: row => ((DemoRow)row).Name,
                valueSetter: (row, value) =>
                {
                    ((DemoRow)row).Name = value?.ToString() ?? string.Empty;
                    return true;
                }));

            DemoGrid.Columns.Add(new GriddoColumnView(
                header: "Score",
                width: 100,
                valueGetter: row => ((DemoRow)row).Score,
                valueSetter: (row, value) =>
                {
                    var text = value?.ToString();
                    if (!double.TryParse(text, out var score))
                    {
                        return false;
                    }

                    ((DemoRow)row).Score = score;
                    return true;
                },
                editor: GriddoCellEditors.Number));

            DemoGrid.Columns.Add(new HtmlGriddoColumnView(
                header: "Html",
                width: 260,
                valueGetter: row => ((DemoRow)row).HtmlSnippet,
                valueSetter: (row, value) =>
                {
                    ((DemoRow)row).HtmlSnippet = value;
                    return true;
                }));

            DemoGrid.Columns.Add(new GriddoColumnView(
                header: "Graphic",
                width: 120,
                valueGetter: row => ((DemoRow)row).Graphic,
                valueSetter: (_, _) => false,
                editor: GriddoCellEditors.Text));

            DemoGrid.Columns.Add(new HostedPlottoColumnView(
                header: PlottoColumnHeader,
                width: 220,
                plottoSeedGetter: row => ((DemoRow)row).PlottoSeed));

            DemoGrid.Columns.Add(new HostedCalibrationPlottoColumnView(
                header: CalibrationColumnHeader,
                width: 240,
                seedGetter: row => ((DemoRow)row).PlottoSeed));

            for (var i = 1; i <= 50_000; i++)
            {
                DemoGrid.Rows.Add(new DemoRow
                {
                    Id = i,
                    Name = $"Item {i:000}",
                    Score = Math.Round(40 + Random.Shared.NextDouble() * 60, 2),
                    HtmlSnippet = i % 3 == 0
                        ? $"<table style=\"width:100%;height:100%;border-collapse:collapse;border:none\"><tr><th>R{i}</th><th>Q{i % 4}</th></tr><tr><td>{i * 2}</td><td><b>{Math.Round(i / 3.0, 2)}</b></td></tr></table>"
                        : $"<b>Row {i}</b> has <i>formatted</i> text",
                    Graphic = CreateTriangle(i),
                    PlottoSeed = i
                });
            }
        }

        /// <summary>Five calibration standards; Y values vary slightly by row seed. Fit mode cycles Linear / LinearThroughOrigin / Quadratic / QuadraticThroughOrigin by seed.</summary>
        internal static List<CalibrationPoint> CreateCalibrationPoints(int seed)
        {
            var r = new Random(seed);
            var xs = new[] { 0.5, 1.0, 2.0, 4.0, 8.0 };
            var list = new List<CalibrationPoint>(5);
            foreach (var x in xs)
            {
                var yIdeal = 0.15 + 0.55 * x + 0.035 * x * x;
                var noise = (r.NextDouble() - 0.5) * 1.1;
                list.Add(new CalibrationPoint
                {
                    X = x,
                    Y = Math.Round(yIdeal + noise, 2),
                    IsEnabled = true
                });
            }

            return list;
        }

        internal static IReadOnlyList<ChartPoint> CreateChromatogramPoints(int seed)
        {
            const int count = 64;
            var points = new ChartPoint[count];
            var r = new Random(seed);
            var peakCenter = 0.2 + r.NextDouble() * 0.6;
            var peakWidth = 0.03 + r.NextDouble() * 0.07;
            var shoulderCenter = Math.Min(0.95, peakCenter + 0.12 + r.NextDouble() * 0.08);
            var shoulderWidth = peakWidth * 1.5;

            for (var i = 0; i < count; i++)
            {
                var x = i / (double)(count - 1);
                var y = 2
                        + 90 * Gaussian(x, peakCenter, peakWidth)
                        + 35 * Gaussian(x, shoulderCenter, shoulderWidth)
                        + r.NextDouble() * 1.5;
                points[i] = new ChartPoint(x, y);
            }

            return points;
        }

        private static double Gaussian(double x, double mean, double sigma)
        {
            var z = (x - mean) / sigma;
            return Math.Exp(-0.5 * z * z);
        }

        private static Geometry CreateTriangle(int seed)
        {
            var size = 5 + (seed % 10);
            var center = 8 + (seed % 5);
            var geometry = new StreamGeometry();
            using var ctx = geometry.Open();
            ctx.BeginFigure(new Point(center, 2), true, true);
            ctx.LineTo(new Point(center + size, 18), true, false);
            ctx.LineTo(new Point(center - size, 18), true, false);
            geometry.Freeze();
            return geometry;
        }
    }

    public sealed class DemoRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public double Score { get; set; }
        public string HtmlSnippet { get; set; } = string.Empty;
        public Geometry Graphic { get; set; } = Geometry.Empty;
        public int PlottoSeed { get; set; }
    }
}
