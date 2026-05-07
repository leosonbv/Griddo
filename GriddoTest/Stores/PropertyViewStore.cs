using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GriddoModelView;

namespace GriddoTest.Stores
{
    public sealed class PropertyViewStore
    {
        private readonly string _filePath;
        private readonly Dictionary<string, PropertyViewConfiguration> _items = new(StringComparer.OrdinalIgnoreCase);

        public PropertyViewStore(string filePath)
        {
            _filePath = filePath;
        }

        public void Load()
        {
            if (!File.Exists(_filePath)) return;
            try
            {
                var json = File.ReadAllText(_filePath);
                var list = JsonSerializer.Deserialize<List<PropertyViewConfiguration>>(json) ?? new();
                _items.Clear();
                foreach (var item in list)
                {
                    var key = $"{item.SourceClassName}|{item.PropertyName}";
                    _items[key] = item;
                }
            }
            catch { /* ignore corrupt file */ }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                var list = new List<PropertyViewConfiguration>(_items.Values);
                var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch { /* ignore */ }
        }

        public void Set(PropertyViewConfiguration config)
        {
            var key = $"{config.SourceClassName}|{config.PropertyName}";
            _items[key] = config;
        }

        public bool TryGet(string sourceClassName, string propertyName, out PropertyViewConfiguration config)
        {
            var key = $"{sourceClassName}|{propertyName}";
            return _items.TryGetValue(key, out config!);
        }
    }
}
