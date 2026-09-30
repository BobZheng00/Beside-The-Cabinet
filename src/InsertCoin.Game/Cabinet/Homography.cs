using Godot;

namespace InsertCoin.Game.Cabinet;

/// <summary>Projective map between the unit square and a quad (TL, TR, BR, BL), for perspective-correct screens.</summary>
public static class Homography
{
    /// <summary>Rows of the 3×3 matrix taking a point in the quad to (u, v) in the unit square.</summary>
    public static (Vector3 Row0, Vector3 Row1, Vector3 Row2) QuadToUnit(IReadOnlyList<Vector2> quad)
    {
        var h = UnitToQuad(quad);
        return Invert(h);
    }

    /// <summary>Unit square → quad (Heckbert). Row-major 3×3.</summary>
    public static double[,] UnitToQuad(IReadOnlyList<Vector2> q)
    {
        double x0 = q[0].X, y0 = q[0].Y, x1 = q[1].X, y1 = q[1].Y, x2 = q[2].X, y2 = q[2].Y, x3 = q[3].X, y3 = q[3].Y;
        double dx1 = x1 - x2, dx2 = x3 - x2, dx3 = x0 - x1 + x2 - x3;
        double dy1 = y1 - y2, dy2 = y3 - y2, dy3 = y0 - y1 + y2 - y3;
        var den = dx1 * dy2 - dx2 * dy1;
        var g = (dx3 * dy2 - dx2 * dy3) / den;
        var h = (dx1 * dy3 - dx3 * dy1) / den;
        return new[,]
        {
            { x1 - x0 + g * x1, x3 - x0 + h * x3, x0 },
            { y1 - y0 + g * y1, y3 - y0 + h * y3, y0 },
            { g, h, 1 },
        };
    }

    public static Vector2 Apply(double[,] m, Vector2 p)
    {
        var w = m[2, 0] * p.X + m[2, 1] * p.Y + m[2, 2];
        return new Vector2((float)((m[0, 0] * p.X + m[0, 1] * p.Y + m[0, 2]) / w),
                           (float)((m[1, 0] * p.X + m[1, 1] * p.Y + m[1, 2]) / w));
    }

    private static (Vector3, Vector3, Vector3) Invert(double[,] m)
    {
        double a = m[0, 0], b = m[0, 1], c = m[0, 2], d = m[1, 0], e = m[1, 1], f = m[1, 2], g = m[2, 0], h = m[2, 1], i = m[2, 2];
        double A = e * i - f * h, B = -(d * i - f * g), C = d * h - e * g;
        var det = a * A + b * B + c * C;
        Vector3 Row(double p, double q, double r) => new((float)(p / det), (float)(q / det), (float)(r / det));
        return (Row(A, -(b * i - c * h), b * f - c * e),
                Row(B, a * i - c * g, -(a * f - c * d)),
                Row(C, -(a * h - b * g), a * e - b * d));
    }
}
