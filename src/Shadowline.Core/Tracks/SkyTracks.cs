using Shadowline.Core.Ephemeris;
using Shadowline.Core.Geometry;
using Shadowline.Core.Horizon;
using Shadowline.Core.Sites;

namespace Shadowline.Core.Tracks;

public sealed record SkyTracks(BitTrack SunVisible, BitTrack EarthVisible, BitTrack EarthOverSun)
{
    public static SkyTracks Build(Site site, BodyEphemeris ephemeris, HorizonProfile horizon, int start = 0, int? length = null)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(ephemeris);
        ArgumentNullException.ThrowIfNull(horizon);

        var count = ephemeris.Grid.Count;
        var frame = new SiteFrame(site);
        var sunVisible = new BitTrack(count);
        var earthVisible = new BitTrack(count);
        var earthOverSun = new BitTrack(count);

        var end = length is { } span ? Math.Min(count, start + span) : count;
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        for (var i = start; i < end; i++)
        {
            var sky = SkySnapshot.At(frame, ephemeris, i);

            sunVisible[i] = sky.Sun.ElevationDeg + sky.SunRadiusDeg > horizon.ElevationAt(sky.Sun.AzimuthDeg);
            earthVisible[i] = sky.Earth.ElevationDeg > horizon.ElevationAt(sky.Earth.AzimuthDeg);
            earthOverSun[i] = sky.EarthOverlapsSun && sunVisible[i];
        }

        return new SkyTracks(sunVisible, earthVisible, earthOverSun);
    }
}
