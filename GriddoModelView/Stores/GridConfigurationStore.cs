using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GriddoModelView.Configuration;

namespace GriddoModelView.Stores;

/// <summary>
/// Eenvoudige JSON-store voor GridConfiguration (per-tabel lay-outs)
/// </summary>
public sealed class GridConfigurationStore
{
    private readonly string _filePath;
    private readonly Dictionary<string, GridConfiguration> _cache = new();

    public GridConfigurationStore(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public void Set(GridConfiguration config)
    {
        _cache[config.Key] = config;
    }

    public bool TryGet(string key, out GridConfiguration? config)
    {
        return _cache.TryGetValue(key, out config);
    }

    public void Save()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(_cache.Values, options);
        File.WriteAllText(_filePath, json);
    }

    private void Load()
    {
        if (!File.Exists(_filePath)) return;

        try
        {
            string json = File.ReadAllText(_filePath);
            var list = JsonSerializer.Deserialize<List<GridConfiguration>>(json) ?? new();
            foreach (var item in list)
            {
                _cache[item.Key] = item;
            }
        }
        catch { /* ignore corrupt file */ }
    }
}
