using Shadowline.Core.Sites;

namespace Shadowline.Core.Geometry;

public sealed class SiteFrame
{
    public SiteFrame(Site site)
    {
        ArgumentNullException.ThrowIfNull(site);

        var lat = site.LatitudeDeg * Math.PI / 180.0;
        var lon = site.LongitudeEastDeg * Math.PI / 180.0;
        var sinLat = Math.Sin(lat);
        var cosLat = Math.Cos(lat);
        var sinLon = Math.Sin(lon);
        var cosLon = Math.Cos(lon);

        Up = new Vec3(cosLat * cosLon, cosLat * sinLon, sinLat);
        East = new Vec3(-sinLon, cosLon, 0.0);
        North = new Vec3(-sinLat * cosLon, -sinLat * sinLon, cosLat);
        Position = Up * (Bodies.MoonRadiusKm + site.ElevationM / 1000.0);
    }

    public Vec3 Position { get; }

    public Vec3 Up { get; }

    public Vec3 East { get; }

    public Vec3 North { get; }

    public SkyPosition Observe(Vec3 bodyFromMoonCenter)
    {
        var d = bodyFromMoonCenter - Position;
        var range = d.Length;
        var east = Vec3.Dot(d, East);
        var north = Vec3.Dot(d, North);
        var up = Vec3.Dot(d, Up);

        var azimuth = Math.Atan2(east, north) * 180.0 / Math.PI;
        if (azimuth < 0)
        {
            azimuth += 360.0;
        }

        var elevation = Math.Asin(Math.Clamp(up / range, -1.0, 1.0)) * 180.0 / Math.PI;
        return new SkyPosition(azimuth, elevation, range);
    }
}
