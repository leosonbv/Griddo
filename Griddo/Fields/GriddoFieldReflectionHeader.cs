using System;
using System.Collections.Generic;
using System.Reflection;

namespace Griddo.Fields;

internal static class GriddoFieldReflectionHeader
{
    /// <summary>
    /// Returns true when <paramref name="memberName"/> resolves on any <paramref name="declaringTypes"/>
    /// to a property with a public instance setter (writable from the grid).
    /// </summary>
    public static bool HasPublicSetterForMember(string memberName, IReadOnlyList<Type>? declaringTypes)
    {
        if (string.IsNullOrEmpty(memberName) || declaringTypes is null || declaringTypes.Count == 0)
        {
            return false;
        }

        foreach (var type in declaringTypes)
        {
            var property = type.GetProperty(
                memberName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property?.GetSetMethod(nonPublic: false) is not null)
            {
                return true;
            }
        }

        return false;
    }
}
