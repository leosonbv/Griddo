namespace Griddo.Fields;

/// <summary>Optional: CLR member name on the record type for tooling (e.g. field chooser “Name” field).</summary>
public interface IGriddoFieldSourceMember
{
    string SourceMemberName { get; }
}

/// <summary>Optional: named source object key in a composite record (e.g. dictionary key) used for tooling labels.</summary>
public interface IGriddoFieldSourceObject
{
    string SourceObjectName { get; }
}
