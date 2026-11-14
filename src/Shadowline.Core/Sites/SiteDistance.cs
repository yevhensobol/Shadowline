using Shadowline.Core.Geometry;

namespace Shadowline.Core.Sites;

public static class SiteDistance
{
    public static double Km(Site a, Site b)
    {
        var lat1 = a.LatitudeDeg * Math.PI / 180.0;
        var lat2 = b.LatitudeDeg * Math.PI / 180.0;
        var dLon = (b.LongitudeEastDeg - a.LongitudeEastDeg) * Math.PI / 180.0;
        var cos = Math.Sin(lat1) * Math.Sin(lat2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
        return Math.Acos(Math.Clamp(cos, -1.0, 1.0)) * Bodies.MoonRadiusKm;
    }
}
