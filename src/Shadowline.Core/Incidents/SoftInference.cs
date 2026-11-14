using Shadowline.Core.Geometry;

namespace Shadowline.Core.Incidents;

public sealed record SoftOptions(double SigmaHours = 6, double Lapse = 0.05);

public sealed record WeightedSample(InferenceSample Sample, double Weight);

public sealed record SoftScore(LanderObservation Observation, double Share, double? MedianErrorHours);

public sealed record SoftResult(int Drawn, double EffectiveSize, double MeanLikelihood, Interval? Azimuth, Interval? Elevation, Interval? Threshold, IReadOnlyList<SoftScore> Scores, IReadOnlyList<WeightedSample> Samples);

public static class SoftInference
{
    private const double SearchHours = 24 * 4;

    public static double DistanceHours(SunTrack track, LanderObservation observation, Vec3 normal, double threshold)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(observation);
        bool On(int i) => AttitudeInference.IsOn(track, i, normal, threshold);
        var from = Math.Clamp(track.IndexOf(observation.FromUtc), 0, track.Count - 1);
        var to = Math.Clamp(track.IndexOf(observation.ToUtc), from, track.Count - 1);
        var step = track.StepHours;
        switch (observation.Kind)
        {
            case ObservationKind.Power:
                for (var i = from; i <= to; i++)
                {
                    if (On(i))
                    {
                        return 0;
                    }
                }

                var reach = (int)(SearchHours / step);
                for (var d = 1; d <= reach; d++)
                {
                    if ((from - d >= 0 && On(from - d)) || (to + d < track.Count && On(to + d)))
                    {
                        return d * step;
                    }
                }

                return double.PositiveInfinity;
            case ObservationKind.NoPower:
                return ShiftHours(track, from, to, i => !On(i));
            case ObservationKind.PowerThroughout:
                return ShiftHours(track, from, to, On);
            case ObservationKind.PowerLost:
                return AttitudeInference.ErrorHours(track, observation, normal, threshold) is { } e ? Math.Abs(e) : double.PositiveInfinity;
            default:
                return 0;
        }
    }

    public static double ShiftHours(SunTrack track, int from, int to, Func<int, bool> holds)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(holds);
        bool All(int a, int b)
        {
            if (a < 0 || b >= track.Count)
            {
                return false;
            }

            for (var i = a; i <= b; i++)
            {
                if (!holds(i))
                {
                    return false;
                }
            }

            return true;
        }

        var reach = (int)(SearchHours / track.StepHours);
        for (var d = 0; d <= reach; d++)
        {
            if (All(from - d, to - d) || All(from + d, to + d))
            {
                return d * track.StepHours;
            }
        }

        return double.PositiveInfinity;
    }

    public static double EventProbability(double distanceHours, SoftOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (double.IsPositiveInfinity(distanceHours))
        {
            return options.Lapse;
        }

        var z = distanceHours / options.SigmaHours;
        return options.Lapse + (1 - options.Lapse) * Math.Exp(-0.5 * z * z);
    }

    public static double Likelihood(SunTrack track, IEnumerable<LanderObservation> observations, InferenceSample sample, SoftOptions options)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(sample);
        var normal = LanderGeometry.Direction(sample.AzimuthDeg, sample.ElevationDeg);
        var l = 1.0;
        foreach (var o in observations.Where(o => o.Fit && AttitudeInference.IsScored(o)))
        {
            l *= EventProbability(DistanceHours(track, o, normal, sample.Threshold), options);
        }

        return l;
    }

    public static SoftResult Run(SunTrack track, IReadOnlyList<LanderObservation> observations, InferencePrior prior, int samples, SoftOptions? options = null, int seed = 1)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(observations);
        var settings = options ?? new SoftOptions();
        var random = new Random(seed);
        var weighted = new List<WeightedSample>(samples);
        for (var s = 0; s < samples; s++)
        {
            var draw = AttitudeInference.Draw(random, prior);
            weighted.Add(new WeightedSample(draw, Likelihood(track, observations, draw, settings)));
        }

        return Summarize(track, observations, weighted);
    }

    public static SoftResult Summarize(SunTrack track, IReadOnlyList<LanderObservation> observations, IReadOnlyList<WeightedSample> weighted)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(weighted);
        var total = weighted.Sum(w => w.Weight);
        var squares = weighted.Sum(w => w.Weight * w.Weight);
        var ess = squares <= 0 ? 0 : total * total / squares;
        var scores = observations.Where(o => o.Check && AttitudeInference.IsScored(o)).Select(o =>
        {
            double hit = 0;
            var errors = new List<(double Value, double Weight)>();
            foreach (var w in weighted)
            {
                var normal = LanderGeometry.Direction(w.Sample.AzimuthDeg, w.Sample.ElevationDeg);
                hit += AttitudeInference.Satisfies(track, o, normal, w.Sample.Threshold) ? w.Weight : 0;
                if (AttitudeInference.ErrorHours(track, o, normal, w.Sample.Threshold) is { } e)
                {
                    errors.Add((e, w.Weight));
                }
            }

            return new SoftScore(o, total <= 0 ? 0 : hit / total, WeightedQuantiles(errors)?.Median);
        }).ToList();
        return new SoftResult(
            weighted.Count,
            ess,
            weighted.Count == 0 ? 0 : total / weighted.Count,
            WeightedCircular(weighted.Select(w => (w.Sample.AzimuthDeg, w.Weight))),
            WeightedQuantiles(weighted.Select(w => (w.Sample.ElevationDeg, w.Weight))),
            WeightedQuantiles(weighted.Select(w => (w.Sample.Threshold, w.Weight))),
            scores,
            weighted);
    }

    public static IReadOnlyList<InferenceSample> Resample(IReadOnlyList<WeightedSample> weighted, int count, int seed = 5)
    {
        ArgumentNullException.ThrowIfNull(weighted);
        var total = weighted.Sum(w => w.Weight);
        if (total <= 0 || weighted.Count == 0)
        {
            return [];
        }

        var random = new Random(seed);
        var cumulative = new double[weighted.Count];
        var run = 0.0;
        for (var i = 0; i < weighted.Count; i++)
        {
            run += weighted[i].Weight / total;
            cumulative[i] = run;
        }

        var result = new List<InferenceSample>(count);
        var start = random.NextDouble() / count;
        var j = 0;
        for (var k = 0; k < count; k++)
        {
            var u = start + (double)k / count;
            while (j < cumulative.Length - 1 && cumulative[j] < u)
            {
                j++;
            }

            result.Add(weighted[j].Sample);
        }

        return result;
    }

    public static Interval? WeightedQuantiles(IEnumerable<(double Value, double Weight)> values)
    {
        var sorted = values.Where(v => v.Weight > 0).OrderBy(v => v.Value).ToArray();
        var total = sorted.Sum(v => v.Weight);
        if (sorted.Length == 0 || total <= 0)
        {
            return null;
        }

        double At(double p)
        {
            var acc = 0.0;
            foreach (var (value, weight) in sorted)
            {
                acc += weight;
                if (acc >= p * total)
                {
                    return value;
                }
            }

            return sorted[^1].Value;
        }

        return new Interval(At(0.05), At(0.5), At(0.95));
    }

    public static Interval? WeightedCircular(IEnumerable<(double Degrees, double Weight)> values)
    {
        var list = values.Where(v => v.Weight > 0).ToList();
        if (list.Count == 0)
        {
            return null;
        }

        var x = list.Sum(v => v.Weight * Math.Cos(v.Degrees * Math.PI / 180));
        var y = list.Sum(v => v.Weight * Math.Sin(v.Degrees * Math.PI / 180));
        var mean = (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
        var q = WeightedQuantiles(list.Select(v => (((v.Degrees - mean + 540) % 360) - 180, v.Weight)))!;
        return new Interval((mean + q.Low + 360) % 360, (mean + q.Median + 360) % 360, (mean + q.High + 360) % 360);
    }
}
