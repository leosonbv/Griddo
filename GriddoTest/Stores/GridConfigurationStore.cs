using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GriddoTest.Stores
{
    public sealed class GridConfigurationStore
    {
        private readonly string _filePath;
        private readonly Dictionary<string, GriddoModelView.GridConfiguration> _items = new(StringComparer.OrdinalIgnoreCase);

        public GridConfigurationStore(string filePath)
        {
            _filePath = filePath;
        }

        public void Load()
        {
            if (!File.Exists(_filePath)) return;
            try
            {
                var json = File.ReadAllText(_filePath);
                var list = JsonSerializer.Deserialize<List<GriddoModelView.GridConfiguration>>(json) ?? new();
                _items.Clear();
                foreach (var item in list)
                {
                    _items[item.Key] = item;
                }
            }
            catch { /* ignore */ }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                var list = new List<GriddoModelView.GridConfiguration>(_items.Values);
                var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch { /* ignore */ }
        }

        public void Set(GriddoModelView.GridConfiguration config)
        {
            if (!string.IsNullOrWhiteSpace(config.Key))
                _items[config.Key] = config;
        }

        public bool TryGet(string key, out GriddoModelView.GridConfiguration config)
        {
            return _items.TryGetValue(key, out config!);
        }
    }
}
