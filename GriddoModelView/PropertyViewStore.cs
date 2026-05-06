using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GriddoModelView;

/// <summary>
/// Eenvoudige JSON-store voor PropertyViewConfiguration (globale metadata)
/// </summary>
public sealed class PropertyViewStore
{
    private readonly string _filePath;
    private readonly Dictionary<string, PropertyViewConfiguration> _cache = new();

    public PropertyViewStore(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public void Set(PropertyViewConfiguration config)
    {
        string key = $"{config.SourceClassName}.{config.PropertyName}";
        _cache[key] = config;
    }

    public bool TryGet(string sourceClassName, string propertyName, out PropertyViewConfiguration? config)
    {
        string key = $"{sourceClassName}.{propertyName}";
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
            var list = JsonSerializer.Deserialize<List<PropertyViewConfiguration>>(json) ?? new();
            foreach (var item in list)
            {
                string key = $"{item.SourceClassName}.{item.PropertyName}";
                _cache[key] = item;
            }
        }
        catch { /* ignore corrupt file */ }
    }
}