using System.Collections.Generic;
using System.IO;
using System.Text.Json;
namespace GriddoModelView;

public sealed class GridConfigurationStore
{
    private readonly string _filePath;
    private readonly Dictionary<string, GridConfiguration> _items = new(StringComparer.OrdinalIgnoreCase);

    public GridConfigurationStore(string filePath)
    {
        _filePath = filePath;
    }

    public void Load()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var list = JsonSerializer.Deserialize<List<GridConfiguration>>(json) ?? new();
            _items.Clear();
            foreach (var item in list)
            {
                _items[item.Key] = item;
            }
        }
        catch
        {
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var list = new List<GridConfiguration>(_items.Values);
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch
        {
        }
    }

    public void Set(GridConfiguration config)
    {
        if (!string.IsNullOrWhiteSpace(config.Key))
        {
            _items[config.Key] = config;
        }
    }

    public bool TryGet(string key, out GridConfiguration config)
    {
        return _items.TryGetValue(key, out config!);
    }
}
