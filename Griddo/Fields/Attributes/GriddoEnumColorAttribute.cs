namespace Griddo.Fields.Attributes;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class GriddoEnumColorAttribute : Attribute
{
    public GriddoEnumColorAttribute(string color)
    {
        Color = color ?? string.Empty;
    }

    public string Color { get; }
}
