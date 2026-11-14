using Shadowline.Core.Geometry;

namespace Shadowline.Core.Incidents;

public enum TipPrior
{
    Uniform,
    OnSide,
}

public sealed record PanelLayout(string Id, IReadOnlyList<LanderPanel> Panels);

public sealed record TipOptions(
    double TotalAreaM2 = 2.0,
    double Efficiency = 0.30,
    double SystemFactor = PowerModel.SystemFactorFixedEfficiency,
    double BatteryWh = 700,
    double StartFraction = 1.0,
    double FloorFraction = 0.2,
    double LoadW = 100,
    double DischargeEfficiency = 0.95,
    TipPrior Prior = TipPrior.Uniform)
{
    public double BatteryHours => LoadW <= 0 ? double.PositiveInfinity : Math.Max(0, BatteryWh * (StartFraction - FloorFraction)) * DischargeEfficiency / LoadW;
}

public sealed record TipCurve(string LayoutId, double[] Survive, double[] MedianHoursToPower, bool[] UprightSurvives);

public sealed record TipRiskResult(DateTimeOffset FirstLandingUtc, double LandingStepHours, int Attitudes, double BatteryHours, IReadOnlyList<TipCurve> Curves);

public static class TipOverRisk
{
    public static IReadOnlyList<PanelLayout> Layouts(double totalAreaM2)
    {
        LanderPanel P(string id, double az, double el, int count) => new(id, az, el, totalAreaM2 / count);
        return
        [
            new("top", [P("top", 0, 90, 1)]),
            new("sides4", [P("n", 0, 0, 4), P("e", 90, 0, 4), P("s", 180, 0, 4), P("w", 270, 0, 4)]),
            new("topSides", [P("top", 0, 90, 5), P("n", 0, 0, 5), P("e", 90, 0, 5), P("s", 180, 0, 5), P("w", 270, 0, 5)]),
            new("cube", [P("top", 0, 90, 6), P("bottom", 0, -90, 6), P("n", 0, 0, 6), P("e", 90, 0, 6), P("s", 180, 0, 6), P("w", 270, 0, 6)]),
        ];
    }

    public static LanderAttitude DrawAttitude(Random random, TipPrior prior)
    {
        ArgumentNullException.ThrowIfNull(random);
        var tilt = prior == TipPrior.OnSide
            ? 60 + random.NextDouble() * 40
            : Math.Acos(1 - 2 * random.NextDouble()) * 180 / Math.PI;
        return new LanderAttitude(tilt, random.NextDouble() * 360, random.NextDouble() * 360);
    }

    public static TipRiskResult Analyze(SunTrack track, int firstLanding, int lastLanding, int landingStride, IReadOnlyList<PanelLayout> layouts, TipOptions options, int attitudes = 600, int seed = 1)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(layouts);
        ArgumentNullException.ThrowIfNull(options);
        firstLanding = Math.Clamp(firstLanding, 0, track.Count - 1);
        lastLanding = Math.Clamp(lastLanding, firstLanding, track.Count - 1);
        landingStride = Math.Max(1, landingStride);
        var batterySteps = options.BatteryHours / track.StepHours;
        var end = (int)Math.Min(track.Count, lastLanding + Math.Ceiling(Math.Min(batterySteps, track.Count)) + 2);
        var span = end - firstLanding;
        var landings = Enumerable.Range(0, (lastLanding - firstLanding) / landingStride + 1).Select(k => firstLanding + k * landingStride).ToArray();
        var random = new Random(seed);
        var draws = Enumerable.Range(0, attitudes).Select(_ => DrawAttitude(random, options.Prior)).ToList();
        var factor = options.Efficiency * options.SystemFactor;
        var curves = new List<TipCurve>();
        foreach (var layout in layouts)
        {
            var survive = new int[landings.Length];
            var waitLists = landings.Select(_ => new List<double>()).ToArray();
            var next = new int[span + 1];
            foreach (var attitude in draws.Prepend(LanderAttitude.Upright))
            {
                var normals = layout.Panels.Select(p => attitude.ToLocal(p.BodyNormal)).ToArray();
                next[span] = int.MaxValue;
                for (var k = span - 1; k >= 0; k--)
                {
                    var i = firstLanding + k;
                    var w = 0.0;
                    if (track.Irradiance[i] > 0)
                    {
                        var d = track.Direction[i];
                        for (var p = 0; p < normals.Length; p++)
                        {
                            var c = Vec3.Dot(d, normals[p]);
                            if (c > 0)
                            {
                                w += c * layout.Panels[p].AreaM2;
                            }
                        }

                        w *= track.Irradiance[i] * factor;
                    }

                    next[k] = w >= options.LoadW ? k : next[k + 1];
                }

                var upright = ReferenceEquals(attitude, LanderAttitude.Upright);
                for (var j = 0; j < landings.Length; j++)
                {
                    var k = landings[j] - firstLanding;
                    var wait = next[k] == int.MaxValue ? double.PositiveInfinity : (next[k] - k) * track.StepHours;
                    if (upright)
                    {
                        continue;
                    }

                    survive[j] += wait <= options.BatteryHours ? 1 : 0;
                    waitLists[j].Add(wait);
                }

                if (upright)
                {
                    var flags = landings.Select(l => next[l - firstLanding] != int.MaxValue && (next[l - firstLanding] - (l - firstLanding)) * track.StepHours <= options.BatteryHours).ToArray();
                    curves.Add(new TipCurve(layout.Id, [], [], flags));
                }
            }

            var uprightFlags = curves[^1].UprightSurvives;
            curves[^1] = new TipCurve(
                layout.Id,
                survive.Select(s => (double)s / attitudes).ToArray(),
                waitLists.Select(l => Median(l)).ToArray(),
                uprightFlags);
        }

        return new TipRiskResult(track.At(firstLanding), track.StepHours * landingStride, attitudes, options.BatteryHours, curves);
    }

    public static double[] HoursToPower(SunTrack track, int landing, PanelLayout layout, TipOptions options, int attitudes = 600, int seed = 1, double maxHours = 24 * 30)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(options);
        var random = new Random(seed);
        var result = new double[attitudes];
        var factor = options.Efficiency * options.SystemFactor;
        var stop = (int)Math.Min(track.Count, landing + maxHours / track.StepHours);
        for (var a = 0; a < attitudes; a++)
        {
            var attitude = DrawAttitude(random, options.Prior);
            var normals = layout.Panels.Select(p => attitude.ToLocal(p.BodyNormal)).ToArray();
            result[a] = double.PositiveInfinity;
            for (var i = landing; i < stop; i++)
            {
                if (track.Irradiance[i] <= 0)
                {
                    continue;
                }

                var w = 0.0;
                for (var p = 0; p < normals.Length; p++)
                {
                    w += Math.Max(0, Vec3.Dot(track.Direction[i], normals[p])) * layout.Panels[p].AreaM2;
                }

                if (w * track.Irradiance[i] * factor >= options.LoadW)
                {
                    result[a] = (i - landing) * track.StepHours;
                    break;
                }
            }
        }

        return result;
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return double.NaN;
        }

        var sorted = values.OrderBy(v => v).ToArray();
        return sorted[sorted.Length / 2];
    }
}
