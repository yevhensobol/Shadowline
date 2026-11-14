using System.Globalization;
using System.Text;
using Shadowline.Core.Ephemeris;
using Shadowline.Core.Geometry;
using Shadowline.Core.Horizon;
using Shadowline.Core.Sites;
using Shadowline.Core.Time;
using Shadowline.Core.Tracks;

namespace Shadowline.Core.Reports;

public static class SiteCsv
{
    public const string Header = "utc,sun_azimuth_deg,sun_elevation_deg,horizon_at_sun_deg,sun_visible,earth_azimuth_deg,earth_elevation_deg,horizon_at_earth_deg,earth_visible,earth_over_sun";

    public static string Build(Site site, BodyEphemeris ephemeris, HorizonProfile horizon, SkyTracks tracks, TimeGrid grid, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(ephemeris);
        ArgumentNullException.ThrowIfNull(horizon);
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(grid);

        var first = Math.Clamp(start, 0, grid.Count);
        var last = Math.Clamp(start + length, first, grid.Count);
        var frame = new SiteFrame(site);
        var text = new StringBuilder(Header.Length * (last - first + 1));
        text.Append(Header).Append('\n');
        for (var i = first; i < last; i++)
        {
            var sky = SkySnapshot.At(frame, ephemeris, i);
            text.Append(grid.At(i).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)).Append(',');
            Append(text, sky.Sun.AzimuthDeg).Append(',');
            Append(text, sky.Sun.ElevationDeg).Append(',');
            Append(text, horizon.ElevationAt(sky.Sun.AzimuthDeg)).Append(',');
            text.Append(tracks.SunVisible[i] ? '1' : '0').Append(',');
            Append(text, sky.Earth.AzimuthDeg).Append(',');
            Append(text, sky.Earth.ElevationDeg).Append(',');
            Append(text, horizon.ElevationAt(sky.Earth.AzimuthDeg)).Append(',');
            text.Append(tracks.EarthVisible[i] ? '1' : '0').Append(',');
            text.Append(tracks.EarthOverSun[i] ? '1' : '0').Append('\n');
        }

        return text.ToString();
    }

    private static StringBuilder Append(StringBuilder text, double value) =>
        text.Append(value.ToString("F3", CultureInfo.InvariantCulture));
}
