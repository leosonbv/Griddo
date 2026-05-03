namespace Griddo.Columns;

/// <summary>Optional: CLR member name on the row type for tooling (e.g. column chooser “Name” column).</summary>
public interface IGriddoColumnSourceMember
{
    string SourceMemberName { get; }
}
