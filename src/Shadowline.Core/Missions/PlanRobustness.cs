using Shadowline.Core.Ephemeris;
using Shadowline.Core.Horizon;
using Shadowline.Core.Incidents;
using Shadowline.Core.Metrics;
using Shadowline.Core.Sites;
using Shadowline.Core.Terrain;
using Shadowline.Core.Time;
using Shadowline.Core.Tracks;

namespace Shadowline.Core.Missions;

public sealed record SlipCheck(bool PlannedMeets, double HoldsForDays, double? NextStartDays, double SearchedDays);

public sealed record MissPointCheck(double EastM, double NorthM, double LatitudeDeg, double LongitudeEastDeg, WindowMetrics Metrics, double SlopeDeg, bool Meets);

public sealed record MissCheck(double RadiusM, double DemSpacingM, bool BelowResolution, IReadOnlyList<MissPointCheck> Points)
{
    public double PassShare => Points.Count == 0 ? 0 : Points.Count(point => point.Meets) / (double)Points.Count;

    public MissPointCheck? Worst => Points.OrderBy(point => point.Metrics.SunFraction).FirstOrDefault();
}

public sealed record TipCheck(string LayoutId, double Share, string BestLayoutId, double BestShare, double BatteryHours, int Attitudes);

public static class PlanRobustness
{
    public static readonly double[] MissRadiiM = [100, 1000, 5000];

    public static int MissionSteps(Mission mission, TimeGrid grid)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(grid);
        return Math.Max(1, (int)Math.Round(mission.Duration / grid.Step));
    }

    public static bool Meets(WindowMetrics metrics, Mission mission)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(mission);
        return metrics.SunFraction >= mission.RequiredSunShare
            && metrics.EarthFraction >= mission.MinEarthShare
            && metrics.LongestDarkness.TotalHours <= mission.MaxDarkHours;
    }

    public static bool MeetsAt(SkyTracks tracks, TimeGrid grid, int start, Mission mission)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        var steps = MissionSteps(mission, grid);
        if (start < 0 || start + steps > grid.Count || !tracks.SunVisible[start])
        {
            return false;
        }

        return Meets(WindowEvaluator.Evaluate(tracks, grid, start, steps), mission);
    }

    public static SlipCheck Slip(SkyTracks tracks, TimeGrid grid, int start, Mission mission, int searchDays = 30)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(grid);
        var stride = Math.Max(1, (int)Math.Round(TimeSpan.FromHours(1) / grid.Step));
        var last = Math.Min(grid.Count - MissionSteps(mission, grid), start + (int)Math.Round(TimeSpan.FromDays(searchDays) / grid.Step));
        var searched = Math.Max(0, last - start) * grid.Step.TotalDays;
        if (start < 0 || start > last)
        {
            return new SlipCheck(false, 0, null, searched);
        }

        if (MeetsAt(tracks, grid, start, mission))
        {
            var held = start;
            for (var s = start + stride; s <= last && MeetsAt(tracks, grid, s, mission); s += stride)
            {
                held = s;
            }

            return new SlipCheck(true, (held - start) * grid.Step.TotalDays, null, searched);
        }

        for (var s = start + stride; s <= last; s += stride)
        {
            if (MeetsAt(tracks, grid, s, mission))
            {
                return new SlipCheck(false, 0, (s - start) * grid.Step.TotalDays, searched);
            }
        }

        return new SlipCheck(false, 0, null, searched);
    }

    public static async Task<MissCheck> MissAsync(Site site, HorizonProfile shipped, TerrainPatch patch, BodyEphemeris ephemeris, int start, Mission mission, double radiusM, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(shipped);
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(ephemeris);
        ArgumentNullException.ThrowIfNull(mission);
        var grid = ephemeris.Grid;
        var steps = MissionSteps(mission, grid);
        var first = Math.Clamp(start, 0, Math.Max(0, grid.Count - steps));
        steps = Math.Min(steps, grid.Count - first);
        var near = patch.Horizon(0, 0);
        var far = new float[HorizonProfile.SampleCount];
        for (var i = 0; i < far.Length; i++)
        {
            var az = i * HorizonProfile.StepDeg;
            far[i] = shipped.ElevationAt(az) > near.ElevationAt(az) + 0.05 ? (float)shipped.ElevationAt(az) : -90f;
        }

        MissPointCheck Evaluate(double east, double north)
        {
            token.ThrowIfCancellationRequested();
            var centre = east == 0 && north == 0;
            var local = centre ? near : patch.Horizon(east, north);
            var merged = new float[HorizonProfile.SampleCount];
            for (var i = 0; i < merged.Length; i++)
            {
                merged[i] = Math.Max((float)local.ElevationAt(i * HorizonProfile.StepDeg), far[i]);
            }

            var distance = Math.Sqrt(east * east + north * north);
            var (lat, lon) = centre ? (site.LatitudeDeg, site.LongitudeEastDeg) : LocalTerrain.Destination(site.LatitudeDeg, site.LongitudeEastDeg, Math.Atan2(east, north), distance);
            var moved = site with { LatitudeDeg = lat, LongitudeEastDeg = lon, ElevationM = site.ElevationM + patch.Height(east, north) - patch.Height(0, 0) };
            var tracks = SkyTracks.Build(moved, ephemeris, new HorizonProfile(merged), first, steps);
            var metrics = WindowEvaluator.Evaluate(tracks, grid, first, steps);
            return new MissPointCheck(east, north, lat, lon, metrics, patch.SlopeDeg(east, north), tracks.SunVisible[first] && Meets(metrics, mission));
        }

        var below = radiusM < patch.SpacingM;
        var points = new List<MissPointCheck> { Evaluate(0, 0) };
        if (!below)
        {
            foreach (var (fraction, count) in Rings)
            {
                for (var k = 0; k < count; k++)
                {
                    var a = 2 * Math.PI * k / count;
                    var r = radiusM * fraction;
                    points.Add(Evaluate(r * Math.Sin(a), r * Math.Cos(a)));
                    await Task.Yield();
                }
            }
        }

        return new MissCheck(radiusM, patch.SpacingM, below, points);
    }

    public static TipCheck Tip(SunTrack track, int landing, Mission mission, int attitudes = 400)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(mission);
        var options = mission.TipOptions();
        var layouts = TipOverRisk.Layouts(mission.PanelAreaM2);
        var shares = layouts
            .Select(layout => (layout.Id, Share: TipOverRisk.Analyze(track, landing, landing, 1, [layout], options, attitudes).Curves[0].Survive[0]))
            .ToList();
        var top = shares[0];
        var best = shares.MaxBy(item => item.Share);
        return new TipCheck(top.Id, top.Share, best.Id, best.Share, options.BatteryHours, attitudes);
    }

    public static TouchdownCheck Touchdown(MissCheck miss, Mission mission, int samples = 4000)
    {
        ArgumentNullException.ThrowIfNull(miss);
        ArgumentNullException.ThrowIfNull(mission);
        var stance = mission.Stance;
        var baseline = 2 * miss.DemSpacingM;
        double Scaled(double slope) => TouchdownStability.FootprintSlopeDeg(slope, baseline, stance.FootprintM);
        var slopes = new List<TouchdownSlope>();
        foreach (var point in miss.Points)
        {
            var centre = point.EastM == 0 && point.NorthM == 0;
            var ring = Math.Sqrt(point.EastM * point.EastM + point.NorthM * point.NorthM) / Math.Max(miss.RadiusM, 1e-9);
            var count = miss.Points.Count(other => Math.Abs(Math.Sqrt(other.EastM * other.EastM + other.NorthM * other.NorthM) / Math.Max(miss.RadiusM, 1e-9) - ring) < 1e-6);
            var weight = miss.Points.Count == 1 || centre ? 1.0 / 16 : ring < 0.75 ? 0.5 / count : 7.0 / 16 / count;
            slopes.Add(new TouchdownSlope(Scaled(point.SlopeDeg), weight));
        }

        var site = miss.Points.Count > 0 ? miss.Points[0].SlopeDeg : 0;
        var scaled = Scaled(site);
        var probability = TouchdownStability.TipProbability(stance, slopes, mission.LateralSpeedMs, samples);
        return new TouchdownCheck(stance.Id, mission.LateralSpeedMs, probability, site, scaled, TouchdownStability.WorstTipSpeed(stance, scaled), baseline, samples);
    }

    private static readonly (double Fraction, int Count)[] Rings = [(0.5, 8), (1.0, 16)];
}
