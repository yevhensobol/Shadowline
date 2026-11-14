using Shadowline.Core.Ephemeris;
using Shadowline.Core.Geometry;
using Shadowline.Core.Tracks;

namespace Shadowline.Core.Comms;

public sealed record GroundStation(string Id, string Name, double LatitudeDeg, double LongitudeEastDeg, double HeightKm);

public static class DeepSpaceNetwork
{
    public const double MinElevationDeg = 10.0;

    private const double Deg = Math.PI / 180.0;

    private const double EquatorKm = 6378.137;

    private const double Flattening = 1 / 298.257223563;

    public static IReadOnlyList<GroundStation> Stations { get; } =
    [
        new("goldstone", "Goldstone", 35.4259, -116.8895, 1.0),
        new("madrid", "Madrid", 40.4313, -4.2480, 0.865),
        new("canberra", "Canberra", -35.4024, 148.9813, 0.69),
    ];

    public static double GmstDeg(DateTimeOffset time)
    {
        var d = (time.UtcDateTime - new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc)).TotalDays;
        var t = d / 36525.0;
        var gmst = 280.46061837 + 360.98564736629 * d + 0.000387933 * t * t - t * t * t / 38710000.0;
        return ((gmst % 360) + 360) % 360;
    }

    public static Vec3 PrecessFromJ2000(Vec3 v, DateTimeOffset time)
    {
        var t = (time.UtcDateTime - new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc)).TotalDays / 36525.0;
        var zeta = (2306.2181 * t + 0.30188 * t * t + 0.017998 * t * t * t) / 3600 * Deg;
        var z = (2306.2181 * t + 1.09468 * t * t + 0.018203 * t * t * t) / 3600 * Deg;
        var theta = (2004.3109 * t - 0.42665 * t * t - 0.041833 * t * t * t) / 3600 * Deg;
        var r = v.Length;
        var alpha = Math.Atan2(v.Y, v.X);
        var delta = Math.Asin(v.Z / r);
        var a = Math.Cos(delta) * Math.Sin(alpha + zeta);
        var b = Math.Cos(theta) * Math.Cos(delta) * Math.Cos(alpha + zeta) - Math.Sin(theta) * Math.Sin(delta);
        var c = Math.Sin(theta) * Math.Cos(delta) * Math.Cos(alpha + zeta) + Math.Cos(theta) * Math.Sin(delta);
        var ra = Math.Atan2(a, b) + z;
        var dec = Math.Asin(Math.Clamp(c, -1, 1));
        return new Vec3(Math.Cos(dec) * Math.Cos(ra), Math.Cos(dec) * Math.Sin(ra), Math.Sin(dec)) * r;
    }

    public static Vec3 MoonFromEarthFixed(Vec3 earthFromMoonMe, DateTimeOffset time)
    {
        var j2000 = MoonOrientation.ToJ2000(earthFromMoonMe * -1, time);
        var ofDate = PrecessFromJ2000(j2000, time);
        var g = -GmstDeg(time) * Deg;
        return new Vec3(Math.Cos(g) * ofDate.X - Math.Sin(g) * ofDate.Y, Math.Sin(g) * ofDate.X + Math.Cos(g) * ofDate.Y, ofDate.Z);
    }

    public static double ElevationDeg(GroundStation station, Vec3 moonFixed)
    {
        var lat = station.LatitudeDeg * Deg;
        var lon = station.LongitudeEastDeg * Deg;
        var e2 = Flattening * (2 - Flattening);
        var n = EquatorKm / Math.Sqrt(1 - e2 * Math.Sin(lat) * Math.Sin(lat));
        var position = new Vec3(
            (n + station.HeightKm) * Math.Cos(lat) * Math.Cos(lon),
            (n + station.HeightKm) * Math.Cos(lat) * Math.Sin(lon),
            (n * (1 - e2) + station.HeightKm) * Math.Sin(lat));
        var up = new Vec3(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
        var look = (moonFixed - position).Normalized();
        return Math.Asin(Math.Clamp(Vec3.Dot(look, up), -1, 1)) / Deg;
    }

    public static double[] Elevations(BodyEphemeris ephemeris, int index)
    {
        var moon = MoonFromEarthFixed(ephemeris.Earth(index), ephemeris.Grid.At(index));
        return Stations.Select(station => ElevationDeg(station, moon)).ToArray();
    }

    public static BitTrack Track(BodyEphemeris ephemeris)
    {
        ArgumentNullException.ThrowIfNull(ephemeris);
        var track = new BitTrack(ephemeris.Grid.Count);
        for (var i = 0; i < ephemeris.Grid.Count; i++)
        {
            var moon = MoonFromEarthFixed(ephemeris.Earth(i), ephemeris.Grid.At(i));
            foreach (var station in Stations)
            {
                if (ElevationDeg(station, moon) >= MinElevationDeg)
                {
                    track[i] = true;
                    break;
                }
            }
        }

        return track;
    }
}
