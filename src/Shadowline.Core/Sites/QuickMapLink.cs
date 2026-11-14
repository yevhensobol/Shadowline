using System.Globalization;

namespace Shadowline.Core.Sites;

public static class QuickMapLink
{
    public const string Base = "https://quickmap.lroc.im-ldi.com/";
    private const double MoonRadiusKm = 1737.4;

    public static int Projection(double latitudeDeg) => latitudeDeg switch
    {
        <= -75 => 27,
        >= 75 => 26,
        _ => 16,
    };

    public static string For(double latitudeDeg, double longitudeEastDeg, double halfWidthKm = 1.5)
    {
        var dLat = halfWidthKm / MoonRadiusKm * 180 / Math.PI;
        var dLon = Math.Min(dLat / Math.Max(Math.Cos(latitudeDeg * Math.PI / 180), 0.02), 30);
        var lon = ((longitudeEastDeg + 180) % 360 + 360) % 360 - 180;
        var south = Math.Max(latitudeDeg - dLat, -90);
        var north = Math.Min(latitudeDeg + dLat, 90);
        string F(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture);
        return $"{Base}?extent={F(lon - dLon)},{F(south)},{F(lon + dLon)},{F(north)}&proj={Projection(latitudeDeg)}";
    }
}
