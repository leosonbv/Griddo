namespace Plotto.Charting.Core;

/// <summary>
/// Builds evaluators y = f(x) for calibration modes (SRP: regression math only).
/// </summary>
public static class CalibrationFitSolver
{
    public static bool TryCreateEvaluator(CalibrationFitMode mode, CalibrationPoint[] pts, out Func<double, double> eval)
    {
        eval = _ => double.NaN;
        switch (mode)
        {
            case CalibrationFitMode.Linear:
                return TryFitLinear(pts, out eval);
            case CalibrationFitMode.LinearThroughOrigin:
                return TryFitLinearThroughOrigin(pts, out eval);
            case CalibrationFitMode.Quadratic:
                return TryFitQuadraticFull(pts, out eval);
            case CalibrationFitMode.QuadraticThroughOrigin:
                return TryFitQuadraticThroughOrigin(pts, out eval);
            default:
                return false;
        }
    }

    public static bool TryFitLinear(CalibrationPoint[] p, out Func<double, double> eval)
    {
        eval = _ => double.NaN;
        if (p.Length < 2)
        {
            return false;
        }

        double sumX = 0, sumY = 0, sumXX = 0, sumXY = 0;
        foreach (var pt in p)
        {
            sumX += pt.X;
            sumY += pt.Y;
            sumXX += pt.X * pt.X;
            sumXY += pt.X * pt.Y;
        }

        var n = p.Length;
        var denom = n * sumXX - sumX * sumX;
        if (Math.Abs(denom) < 1e-18)
        {
            return false;
        }

        var slope = (n * sumXY - sumX * sumY) / denom;
        var intercept = (sumY - slope * sumX) / n;
        eval = x => intercept + slope * x;
        return true;
    }

    public static bool TryFitLinearThroughOrigin(CalibrationPoint[] p, out Func<double, double> eval)
    {
        eval = _ => double.NaN;
        if (p.Length < 1)
        {
            return false;
        }

        double sumXX = 0, sumXY = 0;
        foreach (var pt in p)
        {
            sumXX += pt.X * pt.X;
            sumXY += pt.X * pt.Y;
        }

        if (Math.Abs(sumXX) < 1e-18)
        {
            return false;
        }

        var slope = sumXY / sumXX;
        eval = x => slope * x;
        return true;
    }

    public static bool TryFitQuadraticFull(CalibrationPoint[] p, out Func<double, double> eval)
    {
        eval = _ => double.NaN;
        if (p.Length < 3)
        {
            return false;
        }

        double s0 = p.Length, sx = 0, sx2 = 0, sx3 = 0, sx4 = 0, sy = 0, sxy = 0, sx2y = 0;
        foreach (var pt in p)
        {
            var x = pt.X;
            var y = pt.Y;
            var x2 = x * x;
            sx += x;
            sx2 += x2;
            sx3 += x * x2;
            sx4 += x2 * x2;
            sy += y;
            sxy += x * y;
            sx2y += x2 * y;
        }

        Span<double> m = stackalloc double[9];
        m[0] = s0; m[1] = sx; m[2] = sx2;
        m[3] = sx; m[4] = sx2; m[5] = sx3;
        m[6] = sx2; m[7] = sx3; m[8] = sx4;
        Span<double> rhs = stackalloc double[3];
        rhs[0] = sy;
        rhs[1] = sxy;
        rhs[2] = sx2y;

        if (!Solve3x3(m, rhs, out var a, out var b, out var c))
        {
            return false;
        }

        eval = x => a + b * x + c * x * x;
        return true;
    }

    public static bool TryFitQuadraticThroughOrigin(CalibrationPoint[] p, out Func<double, double> eval)
    {
        eval = _ => double.NaN;
        if (p.Length < 2)
        {
            return false;
        }

        double sx2 = 0, sx3 = 0, sx4 = 0, sxy = 0, sx2y = 0;
        foreach (var pt in p)
        {
            var x = pt.X;
            var y = pt.Y;
            var x2 = x * x;
            sx2 += x2;
            sx3 += x * x2;
            sx4 += x2 * x2;
            sxy += x * y;
            sx2y += x2 * y;
        }

        var det = sx2 * sx4 - sx3 * sx3;
        if (Math.Abs(det) < 1e-18)
        {
            return false;
        }

        var b = (sxy * sx4 - sx2y * sx3) / det;
        var c = (sx2y * sx2 - sxy * sx3) / det;
        eval = x => b * x + c * x * x;
        return true;
    }

    private static bool Solve3x3(Span<double> m, Span<double> rhs, out double a, out double b, out double c)
    {
        a = b = c = 0;
        var work = new double[12];
        for (var i = 0; i < 3; i++)
        {
            for (var j = 0; j < 3; j++)
            {
                work[i * 4 + j] = m[i * 3 + j];
            }

            work[i * 4 + 3] = rhs[i];
        }

        for (var col = 0; col < 3; col++)
        {
            var pivotRecord = col;
            var best = Math.Abs(work[col * 4 + col]);
            for (var r = col + 1; r < 3; r++)
            {
                var v = Math.Abs(work[r * 4 + col]);
                if (v > best)
                {
                    best = v;
                    pivotRecord = r;
                }
            }

            if (best < 1e-18)
            {
                return false;
            }

            if (pivotRecord != col)
            {
                for (var j = 0; j < 4; j++)
                {
                    (work[col * 4 + j], work[pivotRecord * 4 + j]) = (work[pivotRecord * 4 + j], work[col * 4 + j]);
                }
            }

            var div = work[col * 4 + col];
            for (var j = 0; j < 4; j++)
            {
                work[col * 4 + j] /= div;
            }

            for (var r = 0; r < 3; r++)
            {
                if (r == col)
                {
                    continue;
                }

                var f = work[r * 4 + col];
                if (Math.Abs(f) < 1e-18)
                {
                    continue;
                }

                for (var j = 0; j < 4; j++)
                {
                    work[r * 4 + j] -= f * work[col * 4 + j];
                }
            }
        }

        a = work[3];
        b = work[7];
        c = work[11];
        return true;
    }
}
