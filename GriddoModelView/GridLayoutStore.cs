using System.Text.Json;

namespace GriddoModelView;

public sealed class GridLayoutStore
{
    private readonly string _filePath;
    private readonly Dictionary<string, GridLayoutDefinition> _map = new(StringComparer.OrdinalIgnoreCase);

    public GridLayoutStore(string filePath)
    {
        _filePath = filePath;
    }

    public bool TryGet(string gridKey, out GridLayoutDefinition definition)
    {
        if (_map.TryGetValue(gridKey.Trim(), out var found))
        {
            definition = Clone(found);
            return true;
        }

        definition = new GridLayoutDefinition();
        return false;
    }

    public void Set(GridLayoutDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.GridKey))
        {
            return;
        }

        _map[definition.GridKey.Trim()] = Clone(definition);
    }

    public void Load()
    {
        _map.Clear();
        if (!File.Exists(_filePath))
        {
            return;
        }

        var json = File.ReadAllText(_filePath);
        var items = JsonSerializer.Deserialize<List<GridLayoutDefinition>>(json);
        if (items is null)
        {
            return;
        }

        foreach (var item in items)
        {
            Set(item);
        }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var list = _map.Values
            .OrderBy(x => x.GridKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    private static GridLayoutDefinition Clone(GridLayoutDefinition source) => new()
    {
        GridKey = source.GridKey,
        RowHeight = source.RowHeight,
        VisibleRowCount = source.VisibleRowCount,
        FrozenColumns = source.FrozenColumns,
        FrozenRows = source.FrozenRows,
        ShowSelectionColor = source.ShowSelectionColor,
        ShowCurrentCellRect = source.ShowCurrentCellRect,
        ShowRowSelectionColor = source.ShowRowSelectionColor,
        ShowColSelectionColor = source.ShowColSelectionColor,
        ShowEditCellRect = source.ShowEditCellRect,
        ShowSortingIndicators = source.ShowSortingIndicators,
        ShowHorizontalScrollBar = source.ShowHorizontalScrollBar,
        ShowVerticalScrollBar = source.ShowVerticalScrollBar,
        ImmediatePlottoEdit = source.ImmediatePlottoEdit,
        Columns = source.Columns.Select(static c => new GridColumnLayoutDefinition
        {
            SourceColumnIndex = c.SourceColumnIndex,
            Fill = c.Fill,
            Visible = c.Visible,
            Width = c.Width,
            SortPriority = c.SortPriority,
            SortAscending = c.SortAscending
        }).ToList(),
        PlotColumns = source.PlotColumns.Select(static p => new GridPlotColumnLayoutDefinition
        {
            SourceColumnIndex = p.SourceColumnIndex,
            TitleSelection = p.TitleSelection,
            XAxis = p.XAxis,
            YAxis = p.YAxis,
            XAxisTitle = p.XAxisTitle,
            YAxisTitle = p.YAxisTitle,
            Label = p.Label,
            XAxisUnit = p.XAxisUnit,
            YAxisUnit = p.YAxisUnit,
            XAxisLabelPrecision = p.XAxisLabelPrecision,
            YAxisLabelPrecision = p.YAxisLabelPrecision
        }).ToList()
    };
}
