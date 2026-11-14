using Shadowline.Core.Comms;
using Shadowline.Core.Ephemeris;
using Shadowline.Core.Geometry;
using Shadowline.Core.Horizon;
using Shadowline.Core.Metrics;
using Shadowline.Core.Power;
using Shadowline.Core.Sites;
using Shadowline.Core.Terrain;
using Shadowline.Core.Tracks;

namespace Shadowline.Core.Reports;

public sealed record ReportShadow(string Object, double HeightM, double? LengthM);

public sealed record ReportLanding(
    DateTimeOffset Utc,
    double SunElevationDeg,
    double SunAzimuthDeg,
    double SunAboveTerrainDeg,
    double SunDiscVisibleShare,
    double EarthElevationDeg,
    double EarthAzimuthDeg,
    double EarthAboveTerrainDeg,
    bool EarthVisible,
    bool DsnLink,
    IReadOnlyList<string> StationsInView,
    string ApolloLighting,
    IReadOnlyList<ReportShadow> Shadows);

public sealed record ReportChange(string Topic, bool AtLanding, double? HoursToChange);

public sealed record ReportBattery(double CapacityKWh, double MinStateOfCharge, double? HoursToEmpty, double HoursEmpty);

public sealed record ReportSafeHaven(double DarkWithoutEarthHours, double PerLunationHours, double LongestHours, double LimitHoursPerLunation, string Status);

public sealed record ReportSlope(double AtSiteDeg, double MeanDeg, double MaxDeg, double SteepShare, double RadiusM, double BaselineM, double ThresholdDeg);

public sealed record ReportTimeShift(double Hours, double SunlitShare, double LongestDarknessHours, double EarthShare);

public sealed record ReportOffset(string Direction, double DistanceM, double SunlitShare, double LongestDarknessHours, double EarthShare);

public sealed record ReportDispersion(ReportOffset Centre, IReadOnlyList<ReportOffset> Points, double DemSpacingM);

public sealed record ReportEva(double LowSunShare, double LowSunLimitDeg, double? ShadowFactorAtLanding, double? RouteGlareShare, double GlareConeDeg, double NightK);

public sealed record ReportMonth(string Year, int Month, DateTimeOffset StartUtc, double SunlitShare, double LongestDarknessHours, double EarthShare, double LinkShare);

public sealed record ReportMission(
    ReportLanding Landing,
    IReadOnlyList<ReportChange> Changes,
    IReadOnlyList<ReportBattery> Batteries,
    double BatteryLoadW,
    ReportSafeHaven SafeHaven,
    IReadOnlyList<MountEstimate> Mounts,
    ReportSlope? Slope,
    IReadOnlyList<ReportTimeShift> TimeShifts,
    ReportEva Eva);

public static class MissionAnalysis
{
    public const double ApolloMinSunDeg = 7.0;
    public const double ApolloMaxSunDeg = 20.0;
    public const double SafeHavenLimitHours = 50.0;
    public const double SlopeLimitDeg = 15.0;
    public const double LowSunDeg = 5.0;
    public const double GlareConeDeg = 30.0;
    public const double LunationDays = 29.530589;

    public static readonly double[] BatteryKWh = [2, 5, 10, 20, 50];
    public static readonly double[] ShiftHours = [-6, -4, -2, 2, 4, 6];
    public static readonly (string Name, double HeightM)[] ShadowObjects = [("person", 1.8), ("smallLander", 4.0), ("largeLander", 50.0)];
    private static readonly string[] Directions = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];

    public static ReportMission Analyze(
        Site site,
        HorizonProfile horizon,
        BodyEphemeris ephemeris,
        SkyTracks tracks,
        BitTrack dsn,
        int start,
        int length,
        double nightK,
        TerrainPatch? patch,
        IReadOnlyList<double[]>? route)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(horizon);
        ArgumentNullException.ThrowIfNull(ephemeris);
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(dsn);
        var grid = ephemeris.Grid;
        var first = Math.Clamp(start, 0, grid.Count - 1);
        var steps = Math.Clamp(length, 1, grid.Count - first);
        var frame = new SiteFrame(site);
        var link = tracks.EarthVisible.And(dsn);
        var sky = SkySnapshot.At(frame, ephemeris, first);
        var landing = Landing(frame, sky, horizon, ephemeris, tracks, link, first, grid.At(first));
        var hours = grid.Step.TotalHours;

        var changes = new List<ReportChange>
        {
            Change("sun", tracks.SunVisible, first, steps, hours),
            Change("earth", tracks.EarthVisible, first, steps, hours),
            Change("link", link, first, steps, hours),
        };

        var power = PowerModel.Series(frame, ephemeris, horizon, first, steps, PowerOptions.Default, PanelMount.SunTracking, 0);
        int lowSun = 0, sunlit = 0;
        for (var i = 0; i < steps; i++)
        {
            var s = SkySnapshot.At(frame, ephemeris, first + i);
            if (tracks.SunVisible[first + i])
            {
                sunlit++;
                if (s.Sun.ElevationDeg < LowSunDeg)
                {
                    lowSun++;
                }
            }
        }

        var load = PowerOptions.Default.LoadW;
        var batteries = BatteryKWh.Select(capacity => Battery(capacity, load, power, hours, PowerOptions.Default)).ToList();
        var mounts = PowerModel.CompareMounts(frame, ephemeris, horizon, first, steps);

        int darkSteps = 0, darkRun = 0, darkLongest = 0;
        for (var i = 0; i < steps; i++)
        {
            var s = SkySnapshot.At(frame, ephemeris, first + i);
            var halfSun = PowerModel.VisibleFraction(s.Sun.ElevationDeg, horizon.ElevationAt(s.Sun.AzimuthDeg), s.SunRadiusDeg) >= 0.5;
            if (!halfSun && !tracks.EarthVisible[first + i])
            {
                darkSteps++;
                darkRun++;
                darkLongest = Math.Max(darkLongest, darkRun);
            }
            else
            {
                darkRun = 0;
            }
        }

        var darkHours = darkSteps * hours;
        var windowDays = steps * hours / 24.0;
        var perLunation = darkHours / Math.Max(windowDays, 1e-9) * LunationDays;
        var longestDark = darkLongest * hours;
        var status = longestDark > SafeHavenLimitHours ? "fail" : windowDays < LunationDays * 0.99 ? "short" : perLunation <= SafeHavenLimitHours ? "pass" : "fail";
        var safe = new ReportSafeHaven(Math.Round(darkHours, 1), Math.Round(perLunation, 1), longestDark, SafeHavenLimitHours, status);

        ReportSlope? slope = null;
        if (patch is not null)
        {
            var stats = patch.Slopes(patch.SpacingM <= 200 ? 1000 : 3000, SlopeLimitDeg);
            slope = new ReportSlope(Math.Round(stats.AtSiteDeg, 2), Math.Round(stats.MeanDeg, 2), Math.Round(stats.MaxDeg, 2), Math.Round(stats.SteepShare, 4), stats.RadiusM, stats.BaselineM, stats.ThresholdDeg);
        }

        var shifts = new List<ReportTimeShift>();
        foreach (var h in ShiftHours)
        {
            var offset = (int)Math.Round(h * 60 / grid.Step.TotalMinutes);
            var s = first + offset;
            if (s < 0 || s + steps > grid.Count)
            {
                continue;
            }

            var m = WindowEvaluator.Evaluate(tracks, grid, s, steps);
            shifts.Add(new ReportTimeShift(h, m.SunFraction, m.LongestDarkness.TotalHours, m.EarthFraction));
        }

        var eva = new ReportEva(
            sunlit == 0 ? 0 : Math.Round((double)lowSun / sunlit, 4),
            LowSunDeg,
            landing.SunElevationDeg > 0.05 ? Math.Round(1 / Math.Tan(landing.SunElevationDeg * Math.PI / 180), 1) : null,
            route is { Count: > 1 } ? Glare(route, landing.SunAzimuthDeg) : null,
            GlareConeDeg,
            Math.Round(nightK, 1));

        return new ReportMission(landing, changes, batteries, load, safe, mounts, slope, shifts, eva);
    }

    public static async Task<ReportDispersion> DispersionAsync(Site site, HorizonProfile shipped, TerrainPatch patch, BodyEphemeris ephemeris, int start, int length, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(shipped);
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(ephemeris);
        var grid = ephemeris.Grid;
        var first = Math.Clamp(start, 0, grid.Count - 1);
        var steps = Math.Clamp(length, 1, grid.Count - first);
        var near = patch.Horizon(0, 0);
        var far = new float[HorizonProfile.SampleCount];
        for (var i = 0; i < far.Length; i++)
        {
            var az = i * HorizonProfile.StepDeg;
            far[i] = shipped.ElevationAt(az) > near.ElevationAt(az) + 0.05 ? (float)shipped.ElevationAt(az) : -90f;
        }

        ReportOffset Evaluate(string direction, double distance, double east, double north)
        {
            token.ThrowIfCancellationRequested();
            var local = distance == 0 ? near : patch.Horizon(east, north);
            var merged = new float[HorizonProfile.SampleCount];
            for (var i = 0; i < merged.Length; i++)
            {
                merged[i] = Math.Max((float)local.ElevationAt(i * HorizonProfile.StepDeg), far[i]);
            }

            var bearing = Math.Atan2(east, north);
            var (lat, lon) = distance == 0 ? (site.LatitudeDeg, site.LongitudeEastDeg) : LocalTerrain.Destination(site.LatitudeDeg, site.LongitudeEastDeg, bearing, distance);
            var moved = site with { LatitudeDeg = lat, LongitudeEastDeg = lon, ElevationM = site.ElevationM + patch.Height(east, north) - patch.Height(0, 0) };
            var t = SkyTracks.Build(moved, ephemeris, new HorizonProfile(merged), first, steps);
            var m = WindowEvaluator.Evaluate(t, grid, first, steps);
            return new ReportOffset(direction, distance, m.SunFraction, m.LongestDarkness.TotalHours, m.EarthFraction);
        }

        var distances = patch.SpacingM <= 200 ? new[] { 500.0, 1000.0 } : new[] { 1000.0, 2000.0 };
        var centre = Evaluate("centre", 0, 0, 0);
        var points = new List<ReportOffset>();
        foreach (var d in distances)
        {
            for (var k = 0; k < Directions.Length; k++)
            {
                var a = k * Math.PI / 4;
                points.Add(Evaluate(Directions[k], d, d * Math.Sin(a), d * Math.Cos(a)));
                await Task.Yield();
            }
        }

        return new ReportDispersion(centre, points, patch.SpacingM);
    }

    public static IReadOnlyList<ReportMonth> Monthly(string yearId, SkyTracks tracks, BitTrack dsn, Shadowline.Core.Time.TimeGrid grid)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(dsn);
        ArgumentNullException.ThrowIfNull(grid);
        var list = new List<ReportMonth>();
        var link = tracks.EarthVisible.And(dsn);
        var month = new DateTimeOffset(grid.Start.Year, grid.Start.Month, 1, 0, 0, 0, TimeSpan.Zero);
        while (month < grid.End)
        {
            var a = Math.Max(0, grid.IndexOf(month < grid.Start ? grid.Start : month));
            var nextMonth = month.AddMonths(1);
            var b = nextMonth >= grid.End ? grid.Count : grid.IndexOf(nextMonth);
            if (a >= 0 && b > a)
            {
                var m = WindowEvaluator.Evaluate(tracks, grid, a, b - a);
                list.Add(new ReportMonth(yearId, month.Month, month, m.SunFraction, m.LongestDarkness.TotalHours, m.EarthFraction, link.Slice(a, b - a).Fraction()));
            }

            month = nextMonth;
        }

        return list;
    }

    private static ReportLanding Landing(SiteFrame frame, SkySnapshot sky, HorizonProfile horizon, BodyEphemeris ephemeris, SkyTracks tracks, BitTrack link, int index, DateTimeOffset utc)
    {
        var sunEl = sky.Sun.ElevationDeg;
        var elevations = DeepSpaceNetwork.Elevations(ephemeris, index);
        var stations = DeepSpaceNetwork.Stations.Where((_, k) => elevations[k] >= DeepSpaceNetwork.MinElevationDeg).Select(s => s.Name).ToList();
        var rising = index + 1 < ephemeris.Grid.Count && frame.Observe(ephemeris.Sun(index + 1)).ElevationDeg > sunEl;
        var apollo = !tracks.SunVisible[index] ? "dark" : sunEl < ApolloMinSunDeg ? "below" : sunEl > ApolloMaxSunDeg ? "above" : rising ? "within" : "afternoon";
        var shadows = ShadowObjects.Select(o => new ReportShadow(o.Name, o.HeightM, sunEl > 0.05 && tracks.SunVisible[index] ? Math.Round(o.HeightM / Math.Tan(sunEl * Math.PI / 180), 1) : null)).ToList();
        return new ReportLanding(
            utc,
            Math.Round(sunEl, 3),
            Math.Round(sky.Sun.AzimuthDeg, 2),
            Math.Round(sunEl - horizon.ElevationAt(sky.Sun.AzimuthDeg), 3),
            Math.Round(PowerModel.VisibleFraction(sunEl, horizon.ElevationAt(sky.Sun.AzimuthDeg), sky.SunRadiusDeg), 3),
            Math.Round(sky.Earth.ElevationDeg, 3),
            Math.Round(sky.Earth.AzimuthDeg, 2),
            Math.Round(sky.Earth.ElevationDeg - horizon.ElevationAt(sky.Earth.AzimuthDeg), 3),
            tracks.EarthVisible[index],
            link[index],
            stations,
            apollo,
            shadows);
    }

    private static ReportChange Change(string topic, BitTrack track, int first, int steps, double hours)
    {
        var state = track[first];
        for (var i = 1; i < steps; i++)
        {
            if (track[first + i] != state)
            {
                return new ReportChange(topic, state, i * hours);
            }
        }

        return new ReportChange(topic, state, null);
    }

    public static double[] BatterySeries(double capacityKWh, double loadW, IReadOnlyList<double> panelW, double stepHours, PowerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(panelW);
        var settings = options ?? PowerOptions.Ideal;
        var capacity = capacityKWh * 1000;
        var floor = capacity * (1 - settings.UsableDepth);
        var charge = capacity;
        var soc = new double[panelW.Count];
        for (var i = 0; i < panelW.Count; i++)
        {
            soc[i] = charge / capacity;
            var net = panelW[i] - loadW;
            var change = net >= 0 ? net * settings.ChargeEfficiency : net / settings.DischargeEfficiency;
            charge = Math.Clamp(charge + change * stepHours, floor, capacity);
        }

        return soc;
    }

    public static ReportBattery Battery(double capacityKWh, double loadW, IReadOnlyList<double> panelW, double stepHours, PowerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(panelW);
        var settings = options ?? PowerOptions.Ideal;
        var capacity = capacityKWh * 1000;
        var floor = capacity * (1 - settings.UsableDepth);
        var charge = capacity;
        var min = capacity;
        double? empty = null;
        double emptyHours = 0;
        for (var i = 0; i < panelW.Count; i++)
        {
            var net = panelW[i] - loadW;
            var change = net >= 0 ? net * settings.ChargeEfficiency : net / settings.DischargeEfficiency;
            charge = Math.Clamp(charge + change * stepHours, floor, capacity);
            min = Math.Min(min, charge);
            if (charge <= floor + 1e-9 && net < 0)
            {
                empty ??= (i + 1) * stepHours;
                emptyHours += stepHours;
            }
        }

        return new ReportBattery(capacityKWh, Math.Round(min / capacity, 4), empty, emptyHours);
    }

    private static double Glare(IReadOnlyList<double[]> route, double sunAzimuthDeg)
    {
        double total = 0, glare = 0;
        for (var i = 1; i < route.Count; i++)
        {
            var (bearing, distance) = BearingAndDistance(route[i - 1][0], route[i - 1][1], route[i][0], route[i][1]);
            total += distance;
            var diff = Math.Abs(((bearing - sunAzimuthDeg + 540) % 360) - 180);
            if (diff <= GlareConeDeg)
            {
                glare += distance;
            }
        }

        return total <= 0 ? 0 : Math.Round(glare / total, 4);
    }

    public static (double BearingDeg, double DistanceM) BearingAndDistance(double lat1, double lon1, double lat2, double lon2)
    {
        var p1 = lat1 * Math.PI / 180;
        var p2 = lat2 * Math.PI / 180;
        var dl = (lon2 - lon1) * Math.PI / 180;
        var y = Math.Sin(dl) * Math.Cos(p2);
        var x = Math.Cos(p1) * Math.Sin(p2) - Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dl);
        var bearing = (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
        var a = Math.Sin((p2 - p1) / 2) * Math.Sin((p2 - p1) / 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
        var distance = 2 * LocalTerrain.MoonRadiusM * Math.Asin(Math.Min(1, Math.Sqrt(a)));
        return (bearing, distance);
    }
}
