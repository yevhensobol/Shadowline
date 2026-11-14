namespace Shadowline.Core.Geometry;

public readonly record struct SubPoint(double LatitudeDeg, double LongitudeEastDeg)
{
    public static SubPoint Of(Vec3 direction)
    {
        var unit = direction.Normalized();
        var latitude = Math.Asin(Math.Clamp(unit.Z, -1.0, 1.0)) * 180.0 / Math.PI;
        var longitude = Math.Atan2(unit.Y, unit.X) * 180.0 / Math.PI;
        return new SubPoint(latitude, longitude);
    }

    public Vec3 ToUnit()
    {
        var latitude = LatitudeDeg * Math.PI / 180.0;
        var longitude = LongitudeEastDeg * Math.PI / 180.0;
        return new Vec3(Math.Cos(latitude) * Math.Cos(longitude), Math.Cos(latitude) * Math.Sin(longitude), Math.Sin(latitude));
    }

    public IReadOnlyList<SubPoint> Terminator(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 3);
        var axis = ToUnit();
        var helper = Math.Abs(axis.Z) < 0.9 ? new Vec3(0, 0, 1) : new Vec3(1, 0, 0);
        var u = Cross(axis, helper).Normalized();
        var v = Cross(axis, u);
        var points = new SubPoint[count];
        for (var i = 0; i < count; i++)
        {
            var angle = 2.0 * Math.PI * i / count;
            points[i] = Of(u * Math.Cos(angle) + v * Math.Sin(angle));
        }

        return points;
    }

    private static Vec3 Cross(Vec3 a, Vec3 b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}
