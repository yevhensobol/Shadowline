using Shadowline.Core.Geometry;
using Shadowline.Core.Power;

namespace Shadowline.Core.Incidents;

public enum ObservationKind
{
    Power,
    PowerThroughout,
    NoPower,
    PowerLost,
    Operations,
    PowerLow,
    End,
    Hardware,
    Thermal,
}

public sealed record LanderObservation(ObservationKind Kind, DateTimeOffset FromUtc, DateTimeOffset ToUtc, bool Fit, bool Check, string Note);

public sealed record InferencePrior(double AzimuthMinDeg, double AzimuthMaxDeg, double ElevationMinDeg, double ElevationMaxDeg, double ThresholdMin, double ThresholdMax)
{
    public static InferencePrior Sphere { get; } = new(0, 360, -90, 90, 0.02, 0.6);
}

public sealed record InferenceSample(double AzimuthDeg, double ElevationDeg, double Threshold);

public sealed record ObservationScore(LanderObservation Observation, double Share, double? MedianErrorHours, double? LowHours, double? HighHours);

public sealed record Interval(double Low, double Median, double High);

public sealed record InferenceResult(
    int Drawn,
    IReadOnlyList<InferenceSample> Accepted,
    Interval? Azimuth,
    Interval? Elevation,
    Interval? Threshold,
    IReadOnlyList<ObservationScore> Scores);

public static class AttitudeInference
{
    private const double SearchHours = 24 * 4;

    public static bool IsOn(SunTrack track, int index, Vec3 normal, double threshold) =>
        track.Irradiance[index] / PowerModel.SolarConstantW * Math.Max(0, Vec3.Dot(track.Direction[index], normal)) >= threshold;

    public static bool Satisfies(SunTrack track, LanderObservation observation, Vec3 normal, double threshold) =>
        Satisfies(track, observation, i => IsOn(track, i, normal, threshold));

    public static bool Satisfies(SunTrack track, LanderObservation observation, Func<int, bool> on)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(on);
        var (from, to) = Range(track, observation);
        switch (observation.Kind)
        {
            case ObservationKind.Power:
                for (var i = from; i <= to; i++)
                {
                    if (on(i))
                    {
                        return true;
                    }
                }

                return false;
            case ObservationKind.PowerThroughout:
                for (var i = from; i <= to; i++)
                {
                    if (!on(i))
                    {
                        return false;
                    }
                }

                return true;
            case ObservationKind.NoPower:
                for (var i = from; i <= to; i++)
                {
                    if (on(i))
                    {
                        return false;
                    }
                }

                return true;
            case ObservationKind.PowerLost:
                return ErrorHours(track, observation, on) == 0;
            case ObservationKind.Operations:
                var count = 0;
                for (var i = from; i <= to; i++)
                {
                    count += on(i) ? 1 : 0;
                }

                return count * 2 >= to - from + 1;
            default:
                return true;
        }
    }

    public static bool IsScored(LanderObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return observation.Kind is ObservationKind.Power or ObservationKind.PowerThroughout or ObservationKind.NoPower or ObservationKind.PowerLost or ObservationKind.Operations;
    }

    public static double? ErrorHours(SunTrack track, LanderObservation observation, Vec3 normal, double threshold) =>
        ErrorHours(track, observation, i => IsOn(track, i, normal, threshold));

    public static double? ErrorHours(SunTrack track, LanderObservation observation, Func<int, bool> on)
    {
        ArgumentNullException.ThrowIfNull(on);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Kind != ObservationKind.PowerLost)
        {
            return null;
        }

        var (from, to) = Range(track, observation);
        var reach = (int)(SearchHours / track.StepHours);
        var best = (int?)null;
        var bestDistance = int.MaxValue;
        var previous = on(Math.Max(0, from - reach));
        for (var i = Math.Max(0, from - reach) + 1; i <= Math.Min(track.Count - 1, to + reach); i++)
        {
            var now = on(i);
            if (previous && !now)
            {
                var lost = i;
                var distance = lost < from ? from - lost : lost > to ? lost - to : 0;
                if (distance < bestDistance)
                {
                    best = lost;
                    bestDistance = distance;
                }
            }

            previous = now;
        }

        if (best is not { } found)
        {
            return null;
        }

        return found < from ? (found - from) * track.StepHours : found > to ? (found - to) * track.StepHours : 0;
    }

    public static InferenceSample Draw(Random random, InferencePrior prior)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(prior);
        var sinLow = Math.Sin(prior.ElevationMinDeg * Math.PI / 180);
        var sinHigh = Math.Sin(prior.ElevationMaxDeg * Math.PI / 180);
        var az = prior.AzimuthMinDeg + random.NextDouble() * (prior.AzimuthMaxDeg - prior.AzimuthMinDeg);
        var el = Math.Asin(Math.Clamp(sinLow + random.NextDouble() * (sinHigh - sinLow), -1, 1)) * 180 / Math.PI;
        var threshold = prior.ThresholdMin + random.NextDouble() * (prior.ThresholdMax - prior.ThresholdMin);
        return new InferenceSample((az % 360 + 360) % 360, el, threshold);
    }

    public static bool Fits(SunTrack track, IEnumerable<LanderObservation> observations, InferenceSample sample)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(sample);
        var normal = LanderGeometry.Direction(sample.AzimuthDeg, sample.ElevationDeg);
        return observations.Where(o => o.Fit).All(o => Satisfies(track, o, normal, sample.Threshold));
    }

    public static InferenceResult Run(SunTrack track, IReadOnlyList<LanderObservation> observations, InferencePrior prior, int samples, int seed = 1)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(observations);
        var random = new Random(seed);
        var accepted = new List<InferenceSample>();
        for (var s = 0; s < samples; s++)
        {
            var sample = Draw(random, prior);
            if (Fits(track, observations, sample))
            {
                accepted.Add(sample);
            }
        }

        return Summarize(track, observations, accepted, samples);
    }

    public static InferenceResult Summarize(SunTrack track, IReadOnlyList<LanderObservation> observations, IReadOnlyList<InferenceSample> accepted, int drawn)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(accepted);
        var scores = observations.Where(o => o.Check && IsScored(o))
            .Select(o => Score(track, o, accepted))
            .ToList();
        return new InferenceResult(drawn, accepted, CircularInterval(accepted.Select(a => a.AzimuthDeg)), Quantiles(accepted.Select(a => a.ElevationDeg)), Quantiles(accepted.Select(a => a.Threshold)), scores);
    }

    public static ObservationScore Score(SunTrack track, LanderObservation observation, IReadOnlyList<InferenceSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            return new ObservationScore(observation, 0, null, null, null);
        }

        var hits = 0;
        var errors = new List<double>();
        foreach (var sample in samples)
        {
            var normal = LanderGeometry.Direction(sample.AzimuthDeg, sample.ElevationDeg);
            hits += Satisfies(track, observation, normal, sample.Threshold) ? 1 : 0;
            if (ErrorHours(track, observation, normal, sample.Threshold) is { } error)
            {
                errors.Add(error);
            }
        }

        var q = Quantiles(errors);
        return new ObservationScore(observation, (double)hits / samples.Count, q?.Median, q?.Low, q?.High);
    }

    public static Interval? Quantiles(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0)
        {
            return null;
        }

        double At(double p) => sorted[(int)Math.Clamp(Math.Round(p * (sorted.Length - 1)), 0, sorted.Length - 1)];
        return new Interval(At(0.05), At(0.5), At(0.95));
    }

    public static Interval? CircularInterval(IEnumerable<double> degrees)
    {
        var list = degrees.ToList();
        if (list.Count == 0)
        {
            return null;
        }

        var x = list.Sum(d => Math.Cos(d * Math.PI / 180));
        var y = list.Sum(d => Math.Sin(d * Math.PI / 180));
        var mean = (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
        var offsets = list.Select(d => ((d - mean + 540) % 360) - 180);
        var q = Quantiles(offsets)!;
        return new Interval((mean + q.Low + 360) % 360, (mean + q.Median + 360) % 360, (mean + q.High + 360) % 360);
    }

    private static (int From, int To) Range(SunTrack track, LanderObservation observation)
    {
        var from = Math.Clamp(track.IndexOf(observation.FromUtc), 0, track.Count - 1);
        var to = Math.Clamp(track.IndexOf(observation.ToUtc), from, track.Count - 1);
        return (from, to);
    }
}
