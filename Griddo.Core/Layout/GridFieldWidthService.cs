namespace Griddo.Core.Layout;

public static class GridFieldWidthService
{
    public static double ResolveFieldBaseWidth(
        double declaredWidth,
        bool hasOverride,
        double overrideWidth,
        double minFieldWidth,
        double contentScale)
    {
        var logical = hasOverride ? overrideWidth : declaredWidth;
        return Math.Max(minFieldWidth, logical) * contentScale;
    }

    public static double ResolveFillFieldWidth(
        int fillCount,
        double nonFillTotalWidth,
        double viewportAlongFieldAxis,
        double minFieldWidth,
        double contentScale)
    {
        if (fillCount <= 0)
        {
            return minFieldWidth * contentScale;
        }

        var remaining = Math.Max(0, viewportAlongFieldAxis - nonFillTotalWidth);
        var perFill = remaining / fillCount;
        return Math.Max(minFieldWidth * contentScale, perFill);
    }
}
