using System.Globalization;

namespace Shadowline.Core.Sites;

public static class CustomSite
{
    public const string Prefix = "geo:";

    public static Site Create(double latitudeDeg, double longitudeEastDeg)
    {
        if (!IsValid(latitudeDeg, longitudeEastDeg))
        {
            throw new ArgumentOutOfRangeException(nameof(latitudeDeg), "Coordinates out of range");
        }

        var latitude = Math.Round(latitudeDeg, 4);
        var longitude = Math.Round(longitudeEastDeg, 4);
        var id = Prefix + latitude.ToString("0.####", CultureInfo.InvariantCulture) + "," + longitude.ToString("0.####", CultureInfo.InvariantCulture);
        var name = latitude.ToString("0.####", CultureInfo.InvariantCulture) + ", " + longitude.ToString("0.####", CultureInfo.InvariantCulture);
        return new Site(id, name, null, latitude, longitude, 0.0);
    }

    public static bool TryParse(string? id, out Site site)
    {
        site = null!;
        if (id is null || !id.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = id[Prefix.Length..].Split(',');
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
            || !IsValid(latitude, longitude))
        {
            return false;
        }

        site = Create(latitude, longitude);
        return true;
    }

    public static bool IsValid(double latitudeDeg, double longitudeEastDeg) =>
        double.IsFinite(latitudeDeg) && double.IsFinite(longitudeEastDeg)
        && latitudeDeg is >= -90 and <= 90
        && longitudeEastDeg is >= -180 and <= 360;
}
