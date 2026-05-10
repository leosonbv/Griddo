using System.Globalization;
using Griddo.Fields;

namespace Griddo.Hosting.Configuration;

/// <summary>
/// Resolves which column in a field list a title/HTML segment refers to.
/// Prefer <see cref="IGriddoFieldSourceObject"/> + <see cref="IGriddoFieldSourceMember"/> identity;
/// fall back to legacy key (member name / header); index only when in range.
/// </summary>
public static class HostingSegmentFieldResolver
{
    public static int Resolve(
        IReadOnlyList<IGriddoFieldView> allFields,
        string? sourceObjectName,
        string? propertyName,
        string? sourceFieldKey,
        int sourceFieldIndex)
    {
        sourceObjectName = sourceObjectName?.Trim() ?? string.Empty;
        propertyName = propertyName?.Trim() ?? string.Empty;
        sourceFieldKey = sourceFieldKey?.Trim() ?? string.Empty;

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

        if (!string.IsNullOrEmpty(sourceFieldKey))
        {
            var matches = new List<int>();
            for (var i = 0; i < allFields.Count; i++)
            {
                if (string.Equals(LegacyKey(allFields[i], i), sourceFieldKey, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(i);
                }
            }

            if (matches.Count == 1)
            {
                return matches[0];
            }

            if (matches.Count > 1 && !string.IsNullOrEmpty(sourceObjectName))
            {
                foreach (var i in matches)
                {
                    var so = allFields[i] is IGriddoFieldSourceObject o ? o.SourceObjectName.Trim() : string.Empty;
                    if (string.Equals(so, sourceObjectName, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }

            if (matches.Count > 0)
            {
                return matches[0];
            }

            return -1;
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

    private static string LegacyKey(IGriddoFieldView field, int fieldIndex)
    {
        if (field is IGriddoFieldSourceMember sourceMember && !string.IsNullOrWhiteSpace(sourceMember.SourceMemberName))
        {
            return sourceMember.SourceMemberName;
        }

        return !string.IsNullOrWhiteSpace(field.Header)
            ? field.Header
            : fieldIndex.ToString(CultureInfo.InvariantCulture);
    }
}
