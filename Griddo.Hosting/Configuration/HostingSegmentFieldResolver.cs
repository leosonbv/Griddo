using Griddo.Abstractions.Fields;
using Griddo.Fields;

namespace Griddo.Hosting.Configuration;

/// <summary>
/// Resolves which column in a field list a title/HTML segment refers to.
/// Prefers <see cref="IGriddoFieldSourceObject"/> + <see cref="IGriddoFieldSourceMember"/> identity;
/// falls back to index when in range.
/// </summary>
public static class HostingSegmentFieldResolver
{
    /// <summary>
    /// Resolves all <paramref name="segments"/> against <paramref name="resolveFields"/> and
    /// <paramref name="hostingFields"/> in a single pass and returns a per-segment result array.
    /// Index <c>i</c> holds <c>(field, displayField)</c> for segment <c>i</c>, or <c>null</c>
    /// when the segment cannot be resolved or is disabled.
    /// </summary>
    public static (IGriddoFieldView field, IGriddoFieldView displayField)?[] ResolveAll(
        IReadOnlyList<PlotTitleSegmentConfiguration> segments,
        IReadOnlyList<IGriddoFieldView> resolveFields,
        IReadOnlyList<IGriddoFieldView> hostingFields)
    {
        var result = new (IGriddoFieldView field, IGriddoFieldView displayField)?[segments.Count];
        for (var i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            var srcIndex = Resolve(resolveFields, seg.SourceObjectName, seg.PropertyName, seg.SourceFieldIndex);
            if (srcIndex < 0 || srcIndex >= resolveFields.Count)
            {
                continue;
            }

            var field = resolveFields[srcIndex];
            var hostIndex = Resolve(hostingFields, seg.SourceObjectName, seg.PropertyName, seg.SourceFieldIndex);
            var displayField = hostIndex >= 0 && hostIndex < hostingFields.Count ? hostingFields[hostIndex] : field;
            result[i] = (field, displayField);
        }

        return result;
    }

    public static int Resolve(
        IReadOnlyList<IGriddoFieldView> allFields,
        string? sourceObjectName,
        string? propertyName,
        int sourceFieldIndex)
    {
        sourceObjectName = sourceObjectName?.Trim() ?? string.Empty;
        propertyName = propertyName?.Trim() ?? string.Empty;

        if (!string.IsNullOrEmpty(sourceObjectName) && !string.IsNullOrEmpty(propertyName))
        {
            for (var i = 0; i < allFields.Count; i++)
            {
                if (MatchesComposite(allFields[i], sourceObjectName, propertyName))
                {
                    return i;
                }
            }
        }

        if (string.IsNullOrEmpty(sourceObjectName) && !string.IsNullOrEmpty(propertyName))
        {
            var matchIndex = -1;
            for (var i = 0; i < allFields.Count; i++)
            {
                if (allFields[i] is not IGriddoFieldSourceMember member)
                {
                    continue;
                }

                if (!string.Equals(member.SourceMemberName.Trim(), propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (matchIndex >= 0)
                {
                    return -1;
                }

                matchIndex = i;
            }

            if (matchIndex >= 0)
            {
                return matchIndex;
            }
        }

        if (sourceFieldIndex >= 0 && sourceFieldIndex < allFields.Count)
        {
            return sourceFieldIndex;
        }

        return -1;
    }

    private static bool MatchesComposite(IGriddoFieldView field, string sourceObjectName, string propertyName)
    {
        var so = field is IGriddoFieldSourceObject o ? o.SourceObjectName.Trim() : string.Empty;
        var pn = field is IGriddoFieldSourceMember m ? m.SourceMemberName.Trim() : string.Empty;
        return string.Equals(so, sourceObjectName, StringComparison.OrdinalIgnoreCase)
               && string.Equals(pn, propertyName, StringComparison.OrdinalIgnoreCase);
    }
}
