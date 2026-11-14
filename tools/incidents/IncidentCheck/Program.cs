using System.Text.Json;
using Shadowline.Core.Geometry;
using Shadowline.Core.Horizon;
using Shadowline.Core.Incidents;
using Shadowline.Data;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: IncidentCheck <data-dir> [synthetic-count] [draws]");
    return 1;
}

var data = args[0];
var syntheticCount = args.Length > 1 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 200;
var draws = args.Length > 2 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 6000;
await using var stream = File.OpenRead(Path.Combine(data, "incidents.json"));
var document = await IncidentsReader.ReadAsync(stream);
await using var catalogStream = File.OpenRead(Path.Combine(data, "catalog.json"));
var catalog = await SiteCatalogReader.ReadAsync(catalogStream);

SunTrack Track(Incident item, double offset = 0)
{
    var ephemeris = EphemerisReader.Read(File.ReadAllBytes(Path.Combine(data, "incidents", item.Id + ".bin")), item.Grid);
    var file = Path.Combine(data, "horizon", item.Site + ".bin");
    var horizon = File.Exists(file) ? HorizonReader.Read(File.ReadAllBytes(file)) : HorizonProfile.Flat;
    return SunTrack.Build(new SiteFrame(catalog.Find(item.Site)!), ephemeris, horizon, 0, offset);
}

static object Fit(InferenceResult r) => new
{
    drawn = r.Drawn,
    kept = r.Accepted.Count,
    azimuthDeg = r.Azimuth,
    elevationDeg = r.Elevation,
    threshold = r.Threshold,
};

var slim = document.Incidents!.Single(i => i.Id == "slim");
var slimTrack = Track(slim);
var slimObs = slim.Observations!.Select(o => o.ToCore()).ToList();
var main = AttitudeInference.Run(slimTrack, slimObs, InferencePrior.Sphere, draws);
Console.WriteLine($"slim: kept {main.Accepted.Count}/{draws}, az {main.Azimuth}");

var sensitivity = new List<object>();
void Variant(string id, SunTrack track, IReadOnlyList<LanderObservation> obs, InferencePrior prior)
{
    var r = AttitudeInference.Run(track, obs, prior, draws);
    var days = IncidentAnalysis.LunarDays(track, slim.LandingUtc);
    var day2 = IncidentAnalysis.PredictDays(track, r.Accepted, days.Skip(1).Take(1).ToList(), slim.LandingUtc).FirstOrDefault();
    var lost = r.Scores.FirstOrDefault(x => x.Observation.Kind == ObservationKind.PowerLost);
    sensitivity.Add(new
    {
        id,
        fit = Fit(r),
        powerLostShare = lost is null ? (double?)null : Math.Round(lost.Share, 3),
        day1SunsetUtc = days[0].SunsetUtc,
        day2SunriseUtc = days[1].SunriseUtc,
        checks = r.Scores.Select(s => new { kind = s.Observation.Kind.ToString(), s.Observation.FromUtc, share = Math.Round(s.Share, 3) }),
        day2FirstPowerHours = day2?.FirstPowerUtcHours,
    });
    Console.WriteLine($"  {id}: kept {r.Accepted.Count}, az {r.Azimuth?.Median:F0} [{r.Azimuth?.Low:F0},{r.Azimuth?.High:F0}] sunset1 {days[0].SunsetUtc:MM-dd HH:mm} sunrise2 {days[1].SunriseUtc:MM-dd HH:mm}");
}

Variant("base", slimTrack, slimObs, InferencePrior.Sphere);
Variant("thresholdTo30", slimTrack, slimObs, InferencePrior.Sphere with { ThresholdMax = 0.3 });
Variant("thresholdTo90", slimTrack, slimObs, InferencePrior.Sphere with { ThresholdMax = 0.9 });
Variant("windowsEarlier6h", slimTrack, slimObs.Select(o => o.Fit ? o with { FromUtc = o.FromUtc.AddHours(-6), ToUtc = o.ToUtc.AddHours(-6) } : o).ToList(), InferencePrior.Sphere);
Variant("windowsLater6h", slimTrack, slimObs.Select(o => o.Fit ? o with { FromUtc = o.FromUtc.AddHours(6), ToUtc = o.ToUtc.AddHours(6) } : o).ToList(), InferencePrior.Sphere);
Variant("horizonMinus1", Track(slim, -1), slimObs, InferencePrior.Sphere);
Variant("horizonPlus1", Track(slim, 1), slimObs, InferencePrior.Sphere);

var evidence = new List<object>();
double Evidence(SunTrack track, IReadOnlyList<LanderObservation> obs, InferencePrior prior, out int kept)
{
    var r = AttitudeInference.Run(track, obs, prior, draws);
    kept = r.Accepted.Count;
    return (double)kept / draws;
}

var uniform = Evidence(slimTrack, slimObs, InferencePrior.Sphere, out var keptUniform);
foreach (var (id, prior) in new[] { ("uniform", InferencePrior.Sphere), ("reportedWest", new InferencePrior(270, 270, 0, 0, 0.02, 0.6)), ("westHalf", new InferencePrior(180, 360, -90, 90, 0.02, 0.6)), ("upright", new InferencePrior(0, 0, 90, 90, 0.02, 0.6)) })
{
    var e = Evidence(slimTrack, slimObs, prior, out var kept);
    evidence.Add(new { incident = "slim", id, kept, drawn = draws, evidence = Math.Round(e, 4), standardError = Math.Round(Math.Sqrt(e * (1 - e) / draws), 4), bayesFactor = uniform > 0 ? Math.Round(e / uniform, 3) : (double?)null });
    Console.WriteLine($"  evidence slim {id}: {e:F3}");
}

var im1 = document.Incidents!.Single(i => i.Id == "im1");
var im1Track = Track(im1);
var im1Obs = im1.Observations!.Select(o => o.ToCore()).ToList();
var hypotheses = new List<object>();
foreach (var (id, prior) in new[] { ("panel30ToHorizontal", new InferencePrior(0, 360, 60, 60, 0.02, 0.6)), ("panel60ToHorizontal", new InferencePrior(0, 360, 30, 30, 0.02, 0.6)), ("free", InferencePrior.Sphere) })
{
    var r = AttitudeInference.Run(im1Track, im1Obs, prior, draws);
    var check = r.Scores.Single();
    var ev = (double)r.Accepted.Count / draws;
    hypotheses.Add(new { id, fit = Fit(r), evidence = Math.Round(ev, 4), standardError = Math.Round(Math.Sqrt(ev * (1 - ev) / draws), 4), share = Math.Round(check.Share, 3), shareError = Math.Round(Math.Sqrt(check.Share * (1 - check.Share) / Math.Max(1, r.Accepted.Count)), 3), medianErrorHours = check.MedianErrorHours, lowHours = check.LowHours, highHours = check.HighHours });
    Console.WriteLine($"  im1 {id}: kept {r.Accepted.Count}, share {check.Share:F2}, err {check.MedianErrorHours}");
}

var soft = new List<object>();
static double? LogLoss(IEnumerable<double> shares)
{
    var list = shares.ToList();
    return list.Count == 0 ? null : Math.Round(list.Average(p => -Math.Log(Math.Max(0.01, p))), 3);
}

foreach (var (caseId, track, obs, prior, strict) in new[] { ("slim", slimTrack, slimObs, InferencePrior.Sphere, main), ("im1", im1Track, im1Obs, new InferencePrior(0, 360, 60, 60, 0.02, 0.6), AttitudeInference.Run(im1Track, im1Obs, new InferencePrior(0, 360, 60, 60, 0.02, 0.6), draws)) })
{
    var sunUp = new PredictiveModel("sunUp", IncidentAnalysis.SunUp(track), null);
    var sunShares = obs.Where(o => o.Check && AttitudeInference.IsScored(o)).Select(o => IncidentAnalysis.Score(track, sunUp, o).Probability).ToList();
    soft.Add(new { incident = caseId, id = "sunUp", sigmaHours = 0.0, lapse = 0.0, effectiveSize = 0.0, azimuthDeg = (Interval?)null, elevationDeg = (Interval?)null, threshold = (Interval?)null, meanCheckShare = sunShares.Count == 0 ? (double?)null : Math.Round(sunShares.Average(), 3), meanCheckLog = LogLoss(sunShares) });
    soft.Add(new { incident = caseId, id = "strict", sigmaHours = 0.0, lapse = 0.0, effectiveSize = (double)strict.Accepted.Count, azimuthDeg = strict.Azimuth, elevationDeg = strict.Elevation, threshold = strict.Threshold, meanCheckShare = strict.Scores.Count == 0 ? (double?)null : Math.Round(strict.Scores.Average(x => x.Share), 3), meanCheckLog = LogLoss(strict.Scores.Select(x => x.Share)) });
    foreach (var (sigma, lapse) in new[] { (3.0, 0.01), (6.0, 0.05), (12.0, 0.05), (6.0, 0.2) })
    {
        var r = SoftInference.Run(track, obs, prior, draws, new SoftOptions(sigma, lapse));
        soft.Add(new { incident = caseId, id = $"soft{sigma}_{lapse}", sigmaHours = sigma, lapse, effectiveSize = Math.Round(r.EffectiveSize), azimuthDeg = r.Azimuth, elevationDeg = r.Elevation, threshold = r.Threshold, meanCheckShare = r.Scores.Count == 0 ? (double?)null : Math.Round(r.Scores.Average(x => x.Share), 3), meanCheckLog = LogLoss(r.Scores.Select(x => x.Share)) });
        Console.WriteLine($"  soft {caseId} {sigma} {lapse}: ESS {r.EffectiveSize:F0} az {r.Azimuth?.Median:F0}");
    }
}

var landing = slimTrack.IndexOf(slim.LandingUtc);
var slimDays = IncidentAnalysis.LunarDays(slimTrack, slim.LandingUtc);
var day1End = slimTrack.IndexOf(slimDays[0].SunsetUtc);
var day2 = slimDays[1];

object Synthetic(string id, bool misspecified, int maxDelayHours, int seed)
{
    var random = new Random(seed);
    var cases = new List<(bool Az, bool El, bool Tau, bool? First, double AzErr, double PDay2, bool Day2)>();
    for (var n = 0; n < syntheticCount; n++)
    {
        var truth = AttitudeInference.Draw(random, InferencePrior.Sphere);
        var normals = misspecified
            ? new[] { 0.0, -25.0, 25.0 }.Select(d => LanderGeometry.Direction(truth.AzimuthDeg + d, truth.ElevationDeg)).ToArray()
            : [LanderGeometry.Direction(truth.AzimuthDeg, truth.ElevationDeg)];
        var groundView = (1 - normals[0].Z) / 2;
        bool On(int i)
        {
            var e = slimTrack.Irradiance[i] / Shadowline.Core.Power.PowerModel.SolarConstantW;
            if (e <= 0)
            {
                return false;
            }

            var u = normals.Average(nv => Math.Max(0, Vec3.Dot(slimTrack.Direction[i], nv))) * e;
            if (misspecified)
            {
                u += 0.04 * e * Math.Max(0, slimTrack.Direction[i].Z) * groundView;
            }

            return u >= truth.Threshold;
        }

        var obs = new List<LanderObservation>();
        var windowEnd = landing + 5;
        var atLanding = Enumerable.Range(landing, 6).Any(On);
        obs.Add(new LanderObservation(atLanding ? ObservationKind.Power : ObservationKind.NoPower, slimTrack.At(landing), slimTrack.At(windowEnd), true, false, ""));
        var firstOn = Enumerable.Range(windowEnd + 1, Math.Max(0, day1End - windowEnd - 1)).Cast<int?>().FirstOrDefault(i => On(i!.Value));
        if (firstOn is { } f)
        {
            var lastOn = Enumerable.Range(f, day1End - f).Last(On);
            var contact = Math.Min(lastOn, f + random.Next(0, maxDelayHours * 2 + 1));
            obs.Add(new LanderObservation(ObservationKind.Power, slimTrack.At(contact), slimTrack.At(contact + 12), true, false, ""));
        }
        else
        {
            obs.Add(new LanderObservation(ObservationKind.NoPower, slimTrack.At(windowEnd + 1), slimTrack.At(day1End - 1), true, false, ""));
        }

        var r = AttitudeInference.Run(slimTrack, obs, InferencePrior.Sphere, draws / 2, seed * 1000 + n);
        if (r.Accepted.Count == 0)
        {
            continue;
        }

        bool CoversCircular(Interval? i, double v) => i is not null && ((i.Low <= i.High) ? v >= i.Low && v <= i.High : v >= i.Low || v <= i.High);
        bool Covers(Interval? i, double v) => i is not null && v >= i.Low && v <= i.High;
        var prediction = IncidentAnalysis.PredictDays(slimTrack, r.Accepted, [day2], slim.LandingUtc, 300).Single();
        var from2 = slimTrack.IndexOf(day2.SunriseUtc);
        var to2 = slimTrack.IndexOf(day2.SunsetUtc);
        var truthFirst2 = Enumerable.Range(from2, to2 - from2 + 1).Cast<int?>().FirstOrDefault(i => On(i!.Value));
        bool? firstCovered = truthFirst2 is { } t2 ? Covers(prediction.FirstPowerUtcHours, (slimTrack.At(t2) - slim.LandingUtc).TotalHours) : null;
        var azErr = Math.Abs(((r.Azimuth!.Median - truth.AzimuthDeg + 540) % 360) - 180);
        cases.Add((CoversCircular(r.Azimuth, truth.AzimuthDeg), Covers(r.Elevation, truth.ElevationDeg), Covers(r.Threshold, truth.Threshold), firstCovered, azErr, prediction.PowerShare, truthFirst2 is not null));
    }

    double Share(IEnumerable<bool> values) => values.Any() ? Math.Round(values.Average(v => v ? 1.0 : 0.0), 3) : double.NaN;
    var bins = new[] { 0.0, 0.1, 0.3, 0.5, 0.7, 0.9, 1.0001 };
    var reliability = bins.Zip(bins.Skip(1)).Select(b =>
    {
        var inBin = cases.Where(c => c.PDay2 >= b.First && c.PDay2 < b.Second).ToList();
        return new { from = b.First, to = Math.Min(1, b.Second), count = inBin.Count, predicted = inBin.Count == 0 ? (double?)null : Math.Round(inBin.Average(c => c.PDay2), 3), observed = inBin.Count == 0 ? (double?)null : Math.Round(inBin.Average(c => c.Day2 ? 1.0 : 0.0), 3) };
    }).ToList();
    var withFirst = cases.Where(c => c.First is not null).ToList();
    var baseRate = cases.Average(x => x.Day2 ? 1.0 : 0.0);
    var result = new
    {
        id,
        misspecified,
        maxContactDelayHours = maxDelayHours,
        site = "slim",
        count = cases.Count,
        drawsPerCase = draws / 2,
        seed,
        coverage90 = new { azimuth = Share(cases.Select(c => c.Az)), elevation = Share(cases.Select(c => c.El)), threshold = Share(cases.Select(c => c.Tau)), day2FirstPower = Share(withFirst.Select(c => c.First!.Value)) },
        azimuthErrorDeg = AttitudeInference.Quantiles(cases.Select(c => c.AzErr)),
        day2PowerBrier = Math.Round(cases.Average(c => Math.Pow(c.PDay2 - (c.Day2 ? 1 : 0), 2)), 4),
        day2PowerBrierClimatology = Math.Round(cases.Average(c => Math.Pow(baseRate - (c.Day2 ? 1 : 0), 2)), 4),
        reliability,
    };
    Console.WriteLine(JsonSerializer.Serialize(result));
    return result;
}

var synthetic = Synthetic("matched", false, 72, 11);
var misspecifiedRun = Synthetic("misspecified", true, 192, 12);

var output = new
{
    formatVersion = 1,
    builtAtUtc = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
    draws,
    seed = 1,
    slim = new { fit = Fit(main), sensitivity },
    im1 = new { hypotheses },
    evidence,
    soft,
    synthetic,
    syntheticMisspecified = misspecifiedRun,
};
File.WriteAllText(Path.Combine(data, "incidents_checks.json"), JsonSerializer.Serialize(output, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + "\n");
return 0;
