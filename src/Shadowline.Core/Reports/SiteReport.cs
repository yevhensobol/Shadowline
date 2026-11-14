using Shadowline.Core.Comms;
using Shadowline.Core.Ephemeris;
using Shadowline.Core.Geometry;
using Shadowline.Core.Horizon;
using Shadowline.Core.Localization;
using Shadowline.Core.Metrics;
using Shadowline.Core.Power;
using Shadowline.Core.Routes;
using Shadowline.Core.Sensitivity;
using Shadowline.Core.Sites;
using Shadowline.Core.Thermal;
using Shadowline.Core.Time;
using Shadowline.Core.Tracks;

namespace Shadowline.Core.Reports;

public sealed record ReportInterval(DateTimeOffset StartUtc, DateTimeOffset EndUtc, double Hours);

public sealed record ReportSite(string Id, string Name, string? Mission, double LatitudeDeg, double LongitudeEastDeg, double ElevationM, string HorizonSource, string? CoordinateSource, string? CoordinateSourceUrl);

public sealed record ReportHorizon(double MaxElevationDeg, double MaxAzimuthDeg, double MeanElevationDeg, IReadOnlyList<double> ElevationEvery5Deg);

public sealed record ReportSun(
    double SunlitShare,
    double SunlitHours,
    double LongestDarknessHours,
    int Sunrises,
    int Sunsets,
    double ElevationAtStartDeg,
    double MeanElevationDeg,
    double MaxElevationDeg,
    double MinElevationDeg,
    double MaxAboveHorizonDeg,
    double MinDistanceAu,
    double MaxDistanceAu,
    double MinIrradianceWm2,
    double MaxIrradianceWm2,
    double AngularDiameterDeg);

public sealed record ReportStation(string Id, string Name, double MoonAbove10DegShare);

public sealed record ReportEarth(
    double VisibleShare,
    double VisibleHours,
    double LongestGapHours,
    int Earthrises,
    int Earthsets,
    double MeanElevationDeg,
    double MinElevationDeg,
    double MaxElevationDeg,
    double MinAzimuthDeg,
    double MaxAzimuthDeg,
    double MinRangeKm,
    double MaxRangeKm,
    double MinAngularDiameterDeg,
    double MaxAngularDiameterDeg,
    double IlluminatedFractionAtStart,
    double DsnLinkShare,
    double LongestLinkGapHours,
    IReadOnlyList<ReportStation> Stations,
    double SignalDelaySeconds,
    int EclipseCount,
    double EclipseHours);

public sealed record ReportProvenance(string Quantity, string Method, string Accuracy, IReadOnlyList<string> References);

public sealed record ReportRegolith(double MeanSurfaceK, double SubsurfaceK, double? SunlitMinK, double? DarkMinK, double SimpleModelMaxK, string Model, double SlopeDeg = 0, double AspectDeg = 0);

public sealed record ReportVerdict(string Topic, string Level, double Value);

public sealed record ReportShift(int Days, double SunlitShare, double LongestDarknessHours, double EarthShare);

public sealed record ReportPathPoint(DateTimeOffset Utc, double SunAzimuthDeg, double SunElevationDeg, double EarthAzimuthDeg, double EarthElevationDeg);

public sealed record ReportRoute(IReadOnlyList<double[]> Waypoints, RouteSummary Summary);

public sealed record SiteReport(
    string Generator,
    int SchemaVersion,
    DateTimeOffset GeneratedUtc,
    string DataYear,
    string Frame,
    int StepMinutes,
    ReportSite Site,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    IReadOnlyList<ReportVerdict> Verdicts,
    ReportSun Sun,
    ReportEarth Earth,
    PowerEstimate Power,
    PowerOptions PowerOptions,
    ThermalEstimate Thermal,
    ReportRegolith Regolith,
    ReportHorizon Horizon,
    IReadOnlyList<ReportShift> StartShifts,
    IReadOnlyList<ReportInterval> SunIntervals,
    IReadOnlyList<ReportInterval> EarthIntervals,
    IReadOnlyList<ReportInterval> LinkIntervals,
    IReadOnlyList<ReportInterval> EclipseIntervals,
    IReadOnlyList<ReportPathPoint> Path,
    ReportRoute? Route,
    IReadOnlyList<ReportProvenance> Provenance,
    IReadOnlyList<Reference> References,
    ReportMission? Mission = null,
    ReportDispersion? Dispersion = null,
    IReadOnlyList<ReportMonth>? Monthly = null,
    ReportDiviner? Diviner = null,
    ReportSeries? Series = null,
    ReportRobustness? Robustness = null,
    ReportRadio? Radio = null);

public sealed record ReportRadio(double MastM, double ReceiverM, double RangeKm, double VisibleWithin2KmShare, double VisibleWithin10KmShare, double VisibleAreaKm2);

public sealed record ReportMissPoint(double EastM, double NorthM, double LatitudeDeg, double LongitudeEastDeg, double SunlitShare, double EarthShare, double LongestDarknessHours, double SlopeDeg, bool Meets);

public sealed record ReportRobustness(
    double PanelW,
    double LoadW,
    double BatteryWh,
    int DurationDays,
    double MinEarthShare,
    double SafeLoadW,
    DateTimeOffset PlannedStartUtc,
    bool PlannedMeets,
    double SlipDays,
    double? NextStartDays,
    DateTimeOffset CheckedStartUtc,
    double? MissRadiusM,
    double? DemSpacingM,
    bool? MissBelowResolution,
    double? MissPassShare,
    IReadOnlyList<ReportMissPoint>? MissPoints,
    string TipLayout,
    double TipShare,
    string TipBestLayout,
    double TipBestShare,
    double SafeModeBatteryHours,
    int TipAttitudes,
    string? Stance = null,
    double? LateralSpeedMs = null,
    double? TouchdownTipProbability = null,
    double? FootprintSlopeDeg = null,
    double? TipSpeedMs = null,
    double? PowerLossProbability = null);

public sealed record ReportSocSeries(double CapacityKWh, IReadOnlyList<double> Soc);

public sealed record ReportSeries(
    DateTimeOffset StartUtc,
    double StepHours,
    IReadOnlyList<double> SunElevationDeg,
    IReadOnlyList<double> EarthElevationDeg,
    IReadOnlyList<double> SurfaceK,
    IReadOnlyList<double> TrackingW,
    IReadOnlyList<double> BestFixedW,
    double BestFixedAzimuthDeg,
    IReadOnlyList<ReportSocSeries> Battery,
    double LoadW);

public sealed record ReportDiviner(double MeasuredAnnualMaxK, double MeasuredAnnualAverage2cmK, double? IceStabilityDepthM, double? ModelAnnualMaxK, double? ModelAnnualAverage2cmK);

public static class SiteReportBuilder
{
    public const string GeneratorName = "Shadowline";

    public const int Schema = 1;

    public static readonly int[] ShiftDays = [-7, -3, -1, 1, 3, 7, 14];

    public static SiteReport Build(
        Site site,
        string horizonSource,
        HorizonProfile horizon,
        BodyEphemeris ephemeris,
        SkyTracks tracks,
        BitTrack dsn,
        int start,
        int length,
        string dataYear,
        string frame,
        DateTimeOffset generatedUtc,
        ReportRoute? route = null,
        Shadowline.Core.Terrain.TerrainPatch? patch = null)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(horizon);
        ArgumentNullException.ThrowIfNull(ephemeris);
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(dsn);
        var grid = ephemeris.Grid;
        var first = Math.Clamp(start, 0, grid.Count - 1);
        var steps = Math.Clamp(length, 1, grid.Count - first);
        var siteFrame = new SiteFrame(site);
        var metrics = WindowEvaluator.Evaluate(tracks, grid, first, steps);
        var link = tracks.EarthVisible.And(dsn);
        var linkSlice = link.Slice(first, steps);

        double maxSun = double.MinValue, minSun = double.MaxValue, maxAbove = double.MinValue, minEarth = double.MaxValue, maxEarth = double.MinValue;
        double sumSun = 0, sumEarth = 0, minSunRange = double.MaxValue, maxSunRange = 0, minEarthRange = double.MaxValue, maxEarthRange = 0;
        var earthAz0 = SkySnapshot.At(siteFrame, ephemeris, first).Earth.AzimuthDeg;
        double minEarthOffset = 0, maxEarthOffset = 0;
        var stationSteps = new int[DeepSpaceNetwork.Stations.Count];
        var path = new List<ReportPathPoint>();
        var sunEl = new double[steps];
        var earthEl = new double[steps];
        var pathStride = Math.Max(1, steps / 240);
        for (var i = first; i < first + steps; i++)
        {
            var sky = SkySnapshot.At(siteFrame, ephemeris, i);
            maxSun = Math.Max(maxSun, sky.Sun.ElevationDeg);
            minSun = Math.Min(minSun, sky.Sun.ElevationDeg);
            sumSun += sky.Sun.ElevationDeg;
            maxAbove = Math.Max(maxAbove, sky.Sun.ElevationDeg - horizon.ElevationAt(sky.Sun.AzimuthDeg));
            minEarth = Math.Min(minEarth, sky.Earth.ElevationDeg);
            maxEarth = Math.Max(maxEarth, sky.Earth.ElevationDeg);
            sumEarth += sky.Earth.ElevationDeg;
            var offset = ((sky.Earth.AzimuthDeg - earthAz0 + 540) % 360) - 180;
            minEarthOffset = Math.Min(minEarthOffset, offset);
            maxEarthOffset = Math.Max(maxEarthOffset, offset);
            minSunRange = Math.Min(minSunRange, sky.Sun.RangeKm);
            maxSunRange = Math.Max(maxSunRange, sky.Sun.RangeKm);
            minEarthRange = Math.Min(minEarthRange, sky.Earth.RangeKm);
            maxEarthRange = Math.Max(maxEarthRange, sky.Earth.RangeKm);
            var elevations = DeepSpaceNetwork.Elevations(ephemeris, i);
            for (var k = 0; k < elevations.Length; k++)
            {
                if (elevations[k] >= DeepSpaceNetwork.MinElevationDeg)
                {
                    stationSteps[k]++;
                }
            }

            sunEl[i - first] = sky.Sun.ElevationDeg;
            earthEl[i - first] = sky.Earth.ElevationDeg;
            if ((i - first) % pathStride == 0)
            {
                path.Add(new ReportPathPoint(grid.At(i), Round(sky.Sun.AzimuthDeg), Round(sky.Sun.ElevationDeg), Round(sky.Earth.AzimuthDeg), Round(sky.Earth.ElevationDeg)));
            }
        }

        var startSky = SkySnapshot.At(siteFrame, ephemeris, first);
        var eclipses = Intervals(tracks.EarthOverSun, grid, first, steps);
        var sunSlice = tracks.SunVisible.Slice(first, steps);
        var earthSlice = tracks.EarthVisible.Slice(first, steps);
        var hoursPerStep = grid.Step.TotalHours;
        var sun = new ReportSun(
            metrics.SunFraction,
            sunSlice.CountSet() * hoursPerStep,
            metrics.LongestDarkness.TotalHours,
            Edges(sunSlice, true),
            Edges(sunSlice, false),
            Round(startSky.Sun.ElevationDeg),
            Round(sumSun / steps),
            Round(maxSun),
            Round(minSun),
            Round(maxAbove),
            Math.Round(minSunRange / PowerModel.AuKm, 6),
            Math.Round(maxSunRange / PowerModel.AuKm, 6),
            Math.Round(Irradiance(maxSunRange), 1),
            Math.Round(Irradiance(minSunRange), 1),
            Math.Round(2 * startSky.SunRadiusDeg, 4));
        var sunFromEarth = (ephemeris.Sun(first) - ephemeris.Earth(first)).Normalized();
        var moonFromEarth = (ephemeris.Earth(first) * -1).Normalized();
        var earth = new ReportEarth(
            metrics.EarthFraction,
            earthSlice.CountSet() * hoursPerStep,
            metrics.LongestEarthGap.TotalHours,
            Edges(earthSlice, true),
            Edges(earthSlice, false),
            Round(sumEarth / steps),
            Round(minEarth),
            Round(maxEarth),
            Round((earthAz0 + minEarthOffset + 360) % 360),
            Round((earthAz0 + maxEarthOffset + 360) % 360),
            Math.Round(minEarthRange, 0),
            Math.Round(maxEarthRange, 0),
            Math.Round(2 * Math.Asin(Bodies.EarthRadiusKm / maxEarthRange) * 180 / Math.PI, 4),
            Math.Round(2 * Math.Asin(Bodies.EarthRadiusKm / minEarthRange) * 180 / Math.PI, 4),
            Math.Round((1 + Vec3.Dot(sunFromEarth, moonFromEarth)) / 2, 3),
            linkSlice.Fraction(),
            linkSlice.LongestRun(false) * hoursPerStep,
            DeepSpaceNetwork.Stations.Select((station, k) => new ReportStation(station.Id, station.Name, Math.Round((double)stationSteps[k] / steps, 4))).ToList(),
            Math.Round(startSky.Earth.RangeKm / SpeedOfLightKmS, 3),
            eclipses.Count,
            eclipses.Sum(item => item.Hours));
        var power = PowerModel.Evaluate(siteFrame, ephemeris, horizon, grid, first, steps);
        var tilt = patch is null ? (SlopeDeg: 0.0, AspectDeg: 0.0) : patch.Tilt(480);
        var regolith = Regolith1D.Run(siteFrame, ephemeris, horizon, first, steps, null, 4, 2, tilt.SlopeDeg, tilt.AspectDeg);
        var thermal = new ThermalEstimate(regolith.StartK, regolith.MaxK, regolith.MinK);
        var shifts = SensitivityAnalyzer.ByStartShift(tracks, grid, first, steps, ShiftDays)
            .Select(point => new ReportShift((int)point.Offset, point.Metrics.SunFraction, point.Metrics.LongestDarkness.TotalHours, point.Metrics.EarthFraction))
            .ToList();

        var mission = MissionAnalysis.Analyze(site, horizon, ephemeris, tracks, dsn, first, steps, thermal.NightK, patch, route?.Waypoints);
        var tracking = PowerModel.Series(siteFrame, ephemeris, horizon, first, steps, PowerOptions.Default, PanelMount.SunTracking, 0);
        var bestAz = mission.Mounts.Count > 1 ? mission.Mounts[1].AzimuthDeg : 0;
        var fixedBest = PowerModel.Series(siteFrame, ephemeris, horizon, first, steps, PowerOptions.Default, PanelMount.FixedVertical, bestAz);
        var stride = Math.Max(1, (int)Math.Ceiling(steps / 360.0));
        IReadOnlyList<double> Thin(IReadOnlyList<double> values, int digits) => Enumerable.Range(0, (values.Count + stride - 1) / stride).Select(k => Math.Round(values[k * stride], digits)).ToList();
        var series = new ReportSeries(
            grid.At(first),
            stride * grid.Step.TotalHours,
            Thin(sunEl, 2),
            Thin(earthEl, 2),
            Thin(regolith.SurfaceK, 1),
            Thin(tracking, 1),
            Thin(fixedBest, 1),
            bestAz,
            new[] { 5.0, 10.0, 20.0 }.Select(c => new ReportSocSeries(c, Thin(MissionAnalysis.BatterySeries(c, PowerOptions.Default.LoadW, tracking, grid.Step.TotalHours, PowerOptions.Default), 4))).ToList(),
            PowerOptions.Default.LoadW);

        return new SiteReport(
            GeneratorName,
            Schema,
            generatedUtc,
            dataYear,
            frame,
            (int)grid.Step.TotalMinutes,
            new ReportSite(site.Id, site.Name, site.Mission, site.LatitudeDeg, site.LongitudeEastDeg, site.ElevationM, horizonSource, site.Source, site.SourceUrl),
            grid.At(first),
            grid.At(first + steps),
            Verdicts(metrics, linkSlice.Fraction(), power),
            sun,
            earth,
            power,
            PowerOptions.Default,
            thermal,
            new ReportRegolith(Math.Round(regolith.MeanK, 1), Math.Round(regolith.SubsurfaceK, 1), double.IsNaN(regolith.SunlitMinK) ? null : Math.Round(regolith.SunlitMinK, 1), double.IsNaN(regolith.DarkMinK) ? null : Math.Round(regolith.DarkMinK, 1), Math.Round(ThermalModel.Evaluate(siteFrame, ephemeris, horizon, grid, first, steps, site.LatitudeDeg).DayMaxK, 1), "Hayne et al. 2017 one-dimensional regolith model, H = 0.06 m", Math.Round(tilt.SlopeDeg, 1), Math.Round(tilt.AspectDeg, 0)),
            HorizonSummary(horizon),
            shifts,
            Intervals(tracks.SunVisible, grid, first, steps),
            Intervals(tracks.EarthVisible, grid, first, steps),
            Intervals(link, grid, first, steps),
            eclipses,
            path,
            route,
            Provenance(horizonSource, dataYear, site),
            References.All,
            mission,
            Series: series);
    }

    public const double SpeedOfLightKmS = 299_792.458;

    public static IReadOnlyList<ReportProvenance> Provenance(string horizonSource, string dataYear, Site site)
    {
        string Text(string key) => Strings.Get(Strings.English, key);
        var terrain = horizonSource switch
        {
            "polar" => new[] { References.Barker2021.Id, References.Barker2023.Id, References.Glaser2014.Id },
            "polargrid" => new[] { References.Barker2023.Id, References.Lola2017.Id },
            "pole80" => new[] { References.Barker2023.Id, References.Glaser2014.Id, References.Lola2017.Id },
            "global" => new[] { References.Lola2017.Id },
            _ => Array.Empty<string>(),
        };
        return
        [
            new("position", Text("report.method.position"), Text("report.acc.position"), [References.De440.Id, References.Spice.Id, References.Horizons.Id]),
            new("years", Text("report.method.years"), Text("report.acc.years"), [References.De440.Id, References.Iau2015.Id]),
            new("site", (site.Source ?? Text("report.method.siteCustom")) + (site.SourceUrl is { } url ? " " + url : string.Empty), Text("report.acc.site"), []),
            new("elevation", Text("report.method.elevation"), Text("report.acc.elevation"), [References.Lola2010.Id, References.Lola2017.Id]),
            new("horizon", Text("report.method.horizon." + horizonSource), Text("report.acc.horizon." + horizonSource), terrain),
            new("visibility", Text("report.method.visibility"), Text("report.acc.visibility"), [References.Nominal.Id, References.Iau2015.Id, References.Mazarico2011.Id]),
            new("dsn", Text("report.method.dsn"), Text("report.acc.dsn"), [References.Dsn810.Id, References.Wgs84.Id, References.Lieske1977.Id, References.Aoki1982.Id, References.Horizons.Id]),
            new("delay", Text("report.method.delay"), Text("report.acc.delay"), [References.Si2019.Id]),
            new("power", Text("report.method.power"), Text("report.acc.power"), [References.Kopp2011.Id, References.Au2012.Id, References.Fincannon2020.Id]),
            new("thermal", Text("report.method.thermal"), Text("report.acc.thermal"), [References.Hayne2017.Id, References.Vasavada2012.Id, References.Williams2017.Id, References.Paige2010.Id]),
            new("landing", Text("report.method.landing"), Text("report.acc.landing"), [References.Jones1967.Id, References.Ochmo2023.Id, References.Null2023.Id]),
            new("battery", Text("report.method.battery"), Text("report.acc.battery"), [References.Fincannon2020.Id, References.Viper2022.Id]),
            new("safehaven", Text("report.method.safehaven"), Text("report.acc.safehaven"), [References.Viper2022.Id]),
            new("slope", Text("report.method.slope"), Text("report.acc.slope"), [.. terrain, References.Viper2022.Id]),
            new("dispersion", Text("report.method.dispersion"), Text("report.acc.dispersion"), [.. terrain, References.Viper2022.Id]),
            new("monthly", Text("report.method.monthly"), Text("report.acc.monthly"), [References.De440.Id, References.Iau2015.Id]),
            new("eva", Text("report.method.eva"), Text("report.acc.eva"), [References.Ochmo2023.Id, References.Null2023.Id]),
            new("verdicts", Text("report.method.verdicts"), Text("report.acc.verdicts"), []),
            .. site.LatitudeDeg <= -80 ? new ReportProvenance[] { new("radio", Text("report.method.radio"), Text("report.acc.radio"), [References.Santorelli2026.Id, References.LunarRfData.Id, References.Barker2023.Id]) } : [],
        ];
    }

    private static double Irradiance(double sunRangeKm) => PowerModel.SolarConstantW * Math.Pow(PowerModel.AuKm / sunRangeKm, 2);

    private static int Edges(BitTrack slice, bool rising)
    {
        var count = 0;
        for (var i = 1; i < slice.Length; i++)
        {
            if (slice[i] != slice[i - 1] && slice[i] == rising)
            {
                count++;
            }
        }

        return count;
    }

    public static IReadOnlyList<ReportVerdict> Verdicts(WindowMetrics metrics, double linkShare, PowerEstimate power)
    {
        var night = metrics.LongestDarkness.TotalHours;
        return
        [
            new("power", metrics.SunFraction >= 0.7 ? "good" : metrics.SunFraction >= 0.4 ? "limited" : "poor", metrics.SunFraction),
            new("night", night <= 50 ? "good" : night <= 200 ? "limited" : "poor", night),
            new("link", linkShare >= 0.8 ? "good" : linkShare >= 0.5 ? "limited" : "poor", linkShare),
        ];
    }

    public static ReportHorizon HorizonSummary(HorizonProfile horizon)
    {
        ArgumentNullException.ThrowIfNull(horizon);
        double max = double.MinValue, maxAz = 0, sum = 0;
        var every5 = new List<double>();
        for (var k = 0; k < HorizonProfile.SampleCount; k++)
        {
            var az = k * HorizonProfile.StepDeg;
            var value = horizon.ElevationAt(az);
            sum += value;
            if (value > max)
            {
                max = value;
                maxAz = az;
            }

            if (k % 10 == 0)
            {
                every5.Add(Round(value));
            }
        }

        return new ReportHorizon(Round(max), maxAz, Round(sum / HorizonProfile.SampleCount), every5);
    }

    private static List<ReportInterval> Intervals(BitTrack track, TimeGrid grid, int start, int length)
    {
        var list = new List<ReportInterval>();
        var end = start + length;
        foreach (var run in track.Runs(true))
        {
            var a = Math.Max(run.Start, start);
            var b = Math.Min(run.Start + run.Length, end);
            if (b > a)
            {
                list.Add(new ReportInterval(grid.At(a), grid.At(b), (b - a) * grid.Step.TotalHours));
            }
        }

        return list;
    }

    private static double Round(double value) => Math.Round(value, 3);
}
