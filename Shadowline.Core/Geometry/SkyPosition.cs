namespace Shadowline.Core.Geometry;

public readonly record struct SkyPosition(double AzimuthDeg, double ElevationDeg, double RangeKm)
{
    public double AngularRadiusDeg(double bodyRadiusKm) =>
        Math.Asin(Math.Min(1.0, bodyRadiusKm / RangeKm)) * 180.0 / Math.PI;

    public double SeparationDeg(SkyPosition other)
    {
        var a = ToUnit(this);
        var b = ToUnit(other);
        var cos = Math.Clamp(Vec3.Dot(a, b), -1.0, 1.0);
        return Math.Acos(cos) * 180.0 / Math.PI;
    }

    private static Vec3 ToUnit(SkyPosition p)
    {
        var az = p.AzimuthDeg * Math.PI / 180.0;
        var el = p.ElevationDeg * Math.PI / 180.0;
        return new Vec3(Math.Cos(el) * Math.Sin(az), Math.Cos(el) * Math.Cos(az), Math.Sin(el));
    }
}
