namespace Griddo.Columns;

/// <summary>Optional: CLR member name on the row type for tooling (e.g. column chooser “Name” column).</summary>
public interface IGriddoColumnSourceMember
{
    string SourceMemberName { get; }
}

/// <summary>Optional: named source object key in a composite row (e.g. dictionary key) used for tooling labels.</summary>
public interface IGriddoColumnSourceObject
{
    string SourceObjectName { get; }
}
