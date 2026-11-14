using Shadowline.Core.Time;

namespace Shadowline.Core.Geometry;

public static class MoonOrientation
{
    private const double Deg = Math.PI / 180.0;

    public static Vec3 FromJ2000(Vec3 v, DateTimeOffset time)
    {
        var (ra, dec, w) = Angles(time);
        var a = RotateZ(v, (90 + ra) * Deg);
        a = RotateX(a, (90 - dec) * Deg);
        return RotateZ(a, w * Deg);
    }

    public static Vec3 ToJ2000(Vec3 v, DateTimeOffset time)
    {
        var (ra, dec, w) = Angles(time);
        var a = RotateZ(v, -w * Deg);
        a = RotateX(a, -(90 - dec) * Deg);
        return RotateZ(a, -(90 + ra) * Deg);
    }

    private static (double Ra, double Dec, double W) Angles(DateTimeOffset time)
    {
        var d = TimeScales.DaysSinceJ2000Tdb(time);
        var t = d / 36525.0;
        double E(double a, double b) => (a + b * d) * Deg;
        var e1 = E(125.045, -0.0529921);
        var e2 = E(250.089, -0.1059842);
        var e3 = E(260.008, 13.0120009);
        var e4 = E(176.625, 13.3407154);
        var e5 = E(357.529, 0.9856003);
        var e6 = E(311.589, 26.4057084);
        var e7 = E(134.963, 13.0649930);
        var e8 = E(276.617, 0.3287146);
        var e9 = E(34.226, 1.7484877);
        var e10 = E(15.134, -0.1589763);
        var e11 = E(119.743, 0.0036096);
        var e12 = E(239.961, 0.1643573);
        var e13 = E(25.053, 12.9590088);
        var ra = 269.9949 + 0.0031 * t - 3.8787 * Math.Sin(e1) - 0.1204 * Math.Sin(e2) + 0.0700 * Math.Sin(e3) - 0.0172 * Math.Sin(e4)
            + 0.0072 * Math.Sin(e6) - 0.0052 * Math.Sin(e10) + 0.0043 * Math.Sin(e13);
        var dec = 66.5392 + 0.0130 * t + 1.5419 * Math.Cos(e1) + 0.0239 * Math.Cos(e2) - 0.0278 * Math.Cos(e3) + 0.0068 * Math.Cos(e4)
            - 0.0029 * Math.Cos(e6) + 0.0009 * Math.Cos(e7) + 0.0008 * Math.Cos(e10) - 0.0009 * Math.Cos(e13);
        var w = 38.3213 + 13.17635815 * d - 1.4e-12 * d * d + 3.5610 * Math.Sin(e1) + 0.1208 * Math.Sin(e2) - 0.0642 * Math.Sin(e3)
            + 0.0158 * Math.Sin(e4) + 0.0252 * Math.Sin(e5) - 0.0066 * Math.Sin(e6) - 0.0047 * Math.Sin(e7) - 0.0046 * Math.Sin(e8)
            + 0.0028 * Math.Sin(e9) + 0.0052 * Math.Sin(e10) + 0.0040 * Math.Sin(e11) + 0.0019 * Math.Sin(e12) - 0.0044 * Math.Sin(e13);
        return (ra, dec, w);
    }

    public static Vec3 RaDec(double raDeg, double decDeg)
    {
        var ra = raDeg * Deg;
        var dec = decDeg * Deg;
        return new Vec3(Math.Cos(dec) * Math.Cos(ra), Math.Cos(dec) * Math.Sin(ra), Math.Sin(dec));
    }

    private static Vec3 RotateZ(Vec3 v, double angle) =>
        new(Math.Cos(angle) * v.X + Math.Sin(angle) * v.Y, -Math.Sin(angle) * v.X + Math.Cos(angle) * v.Y, v.Z);

    private static Vec3 RotateX(Vec3 v, double angle) =>
        new(v.X, Math.Cos(angle) * v.Y + Math.Sin(angle) * v.Z, -Math.Sin(angle) * v.Y + Math.Cos(angle) * v.Z);
}
