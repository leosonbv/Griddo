using Griddo.Hosting.Configuration;

namespace Griddo.Hosting.Plot;

internal static class PlotHostedVisibility
{
    internal static bool HasEnabledSegments(IReadOnlyList<PlotTitleSegmentConfiguration>? segments)
    {
        if (segments is not { Count: > 0 })
        {
            return false;
        }

        for (var i = 0; i < segments.Count; i++)
        {
            if (segments[i].Enabled)
            {
                return true;
            }
        }

        return false;
    }
}
