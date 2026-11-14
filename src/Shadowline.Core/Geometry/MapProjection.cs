namespace Shadowline.Core.Geometry;

public readonly record struct MapPoint(double X, double Y);

public sealed class EquirectangularProjection(double width, double height)
{
    public double Width { get; } = width;

    public double Height { get; } = height;

    public MapPoint Project(double latitudeDeg, double longitudeEastDeg)
    {
        var longitude = NormalizeLongitude(longitudeEastDeg);
        return new MapPoint((longitude + 180.0) / 360.0 * Width, (90.0 - latitudeDeg) / 180.0 * Height);
    }

    public SubPoint Unproject(double x, double y) =>
        new(90.0 - y / Height * 180.0, x / Width * 360.0 - 180.0);

    public static double NormalizeLongitude(double longitudeDeg)
    {
        var wrapped = (longitudeDeg + 180.0) % 360.0;
        if (wrapped < 0)
        {
            wrapped += 360.0;
        }

        return wrapped - 180.0;
    }
}

public sealed class SouthPolarProjection(double radius, double limitLatitudeDeg = -60.0)
{
    private readonly double _scale = Math.Tan((90.0 + limitLatitudeDeg) * Math.PI / 360.0);

    public double Radius { get; } = radius;

    public double LimitLatitudeDeg { get; } = limitLatitudeDeg;

    public bool Contains(double latitudeDeg) => latitudeDeg <= LimitLatitudeDeg;

    public MapPoint Project(double latitudeDeg, double longitudeEastDeg)
    {
        var r = Radius * Math.Tan((90.0 + latitudeDeg) * Math.PI / 360.0) / _scale;
        var longitude = longitudeEastDeg * Math.PI / 180.0;
        return new MapPoint(Radius + r * Math.Sin(longitude), Radius - r * Math.Cos(longitude));
    }

    public SubPoint Unproject(double x, double y)
    {
        var dx = x - Radius;
        var dy = Radius - y;
        var r = Math.Sqrt(dx * dx + dy * dy) / Radius;
        var latitude = 2.0 * Math.Atan(r * _scale) * 180.0 / Math.PI - 90.0;
        var longitude = Math.Atan2(dx, dy) * 180.0 / Math.PI;
        return new SubPoint(latitude, longitude);
    }
}
