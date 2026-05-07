using Griddo.Fields;

namespace Griddo.Hosting.Fluent;

public sealed class GriddoBuilder
{
    private readonly List<IGriddoFieldView> _fields = [];

    public GriddoBuilder AddField(IGriddoFieldView field)
    {
        _fields.Add(field);
        return this;
    }

    public GriddoBuilder AddFields(IEnumerable<IGriddoFieldView> fields)
    {
        _fields.AddRange(fields);
        return this;
    }

    public IReadOnlyList<IGriddoFieldView> BuildFields() => _fields.AsReadOnly();
}
