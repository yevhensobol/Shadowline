using Shadowline.Core.Ephemeris;
using Shadowline.Core.Geometry;
using Shadowline.Core.Horizon;
using Shadowline.Core.Metrics;
using Shadowline.Core.Sites;
using Shadowline.Core.Terrain;
using Shadowline.Core.Time;
using Shadowline.Core.Tracks;

namespace Shadowline.Core.Sensitivity;

public readonly record struct SensitivityPoint(double Offset, WindowMetrics Metrics);

public static class SensitivityAnalyzer
{
    public static IReadOnlyList<SensitivityPoint> ByStartShift(
        SkyTracks tracks,
        TimeGrid grid,
        int startIndex,
        int lengthSteps,
        IEnumerable<int> shiftDays)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(grid);
        var stepsPerDay = (int)Math.Round(TimeSpan.FromDays(1) / grid.Step);
        var points = new List<SensitivityPoint>();
        foreach (var days in shiftDays)
        {
            var start = startIndex + days * stepsPerDay;
            if (start < 0 || start + lengthSteps > grid.Count)
            {
                continue;
            }

            points.Add(new SensitivityPoint(days, WindowEvaluator.Evaluate(tracks, grid, start, lengthSteps)));
        }

        return points;
    }

    public static IReadOnlyList<SensitivityPoint> ByPosition(
        Site site,
        BodyEphemeris ephemeris,
        HorizonProfile horizon,
        int startIndex,
        int lengthSteps,
        IEnumerable<double> offsetsKm,
        bool alongNorth)
    {
        ArgumentNullException.ThrowIfNull(horizon);
        return ByPosition(site, ephemeris, _ => horizon, startIndex, lengthSteps, offsetsKm, alongNorth);
    }

    public static IReadOnlyList<SensitivityPoint> ByPosition(
        Site site,
        BodyEphemeris ephemeris,
        Func<Site, HorizonProfile> horizonFor,
        int startIndex,
        int lengthSteps,
        IEnumerable<double> offsetsKm,
        bool alongNorth)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(ephemeris);
        ArgumentNullException.ThrowIfNull(horizonFor);
        ArgumentNullException.ThrowIfNull(offsetsKm);
        var points = new List<SensitivityPoint>();
        foreach (var km in offsetsKm)
        {
            var shifted = Shifted(site, km, alongNorth);
            var tracks = SkyTracks.Build(shifted, ephemeris, horizonFor(shifted), startIndex, lengthSteps);
            points.Add(new SensitivityPoint(km, WindowEvaluator.Evaluate(tracks, ephemeris.Grid, startIndex, lengthSteps)));
        }

        return points;
    }

    public static Site Shifted(Site site, double km, bool alongNorth)
    {
        ArgumentNullException.ThrowIfNull(site);
        var bearing = (alongNorth ? 0.0 : Math.PI / 2) + (km < 0 ? Math.PI : 0.0);
        var (latitude, longitude) = km == 0 ? (site.LatitudeDeg, site.LongitudeEastDeg) : LocalTerrain.Destination(site.LatitudeDeg, site.LongitudeEastDeg, bearing, Math.Abs(km) * 1000.0);
        return CustomSite.Create(Math.Clamp(latitude, -90, 90), EquirectangularProjection.NormalizeLongitude(longitude));
    }
}
