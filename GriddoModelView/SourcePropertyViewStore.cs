using System.Text.Json;

namespace GriddoModelView;

public sealed class SourcePropertyViewStore
{
    private readonly string _filePath;
    private readonly Dictionary<string, SourcePropertyViewDefinition> _map = new(StringComparer.OrdinalIgnoreCase);

    public SourcePropertyViewStore(string filePath)
    {
        _filePath = filePath;
    }

    public IReadOnlyCollection<SourcePropertyViewDefinition> Items => _map.Values;

    public bool TryGet(string sourceClassName, string propertyName, out SourcePropertyViewDefinition definition)
    {
        var key = BuildKey(sourceClassName, propertyName);
        if (_map.TryGetValue(key, out var found))
        {
            definition = Clone(found);
            return true;
        }

        definition = new SourcePropertyViewDefinition();
        return false;
    }

    public void Set(SourcePropertyViewDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.SourceClassName) || string.IsNullOrWhiteSpace(definition.PropertyName))
        {
            return;
        }

        var key = BuildKey(definition.SourceClassName, definition.PropertyName);
        _map[key] = Clone(definition);
    }

    public void Load()
    {
        _map.Clear();
        if (!File.Exists(_filePath))
        {
            return;
        }

        var json = File.ReadAllText(_filePath);
        var items = JsonSerializer.Deserialize<List<SourcePropertyViewDefinition>>(json);
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
            .OrderBy(x => x.SourceClassName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.PropertyName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    private static string BuildKey(string sourceClassName, string propertyName)
        => $"{sourceClassName.Trim()}::{propertyName.Trim()}";

    private static SourcePropertyViewDefinition Clone(SourcePropertyViewDefinition source) => new()
    {
        SourceClassName = source.SourceClassName,
        PropertyName = source.PropertyName,
        Header = source.Header,
        AbbreviatedHeader = source.AbbreviatedHeader,
        Description = source.Description,
        StringFormat = source.StringFormat,
        FontSize = source.FontSize,
        FontStyle = source.FontStyle,
        ForegroundColor = source.ForegroundColor,
        BackgroundColor = source.BackgroundColor
    };
}
