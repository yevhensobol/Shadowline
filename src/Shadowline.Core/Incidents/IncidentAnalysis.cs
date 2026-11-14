using Shadowline.Core.Geometry;

namespace Shadowline.Core.Incidents;

public sealed record LunarDay(int Index, DateTimeOffset SunriseUtc, DateTimeOffset SunsetUtc, bool SunriseBeforeData = false, bool SunsetAfterData = false);

public sealed record DayPrediction(LunarDay Day, double PowerShare, Interval? FirstPowerUtcHours, Interval? LastPowerUtcHours);

public sealed record PredictiveModel(string Id, Func<int, bool>? On, IReadOnlyList<InferenceSample>? Samples);

public sealed record ModelScore(string ModelId, LanderObservation Observation, double Probability, double Brier, double LogScore, double? MedianErrorHours);

public static class IncidentAnalysis
{
    public const double LogFloor = 0.01;

    public static IReadOnlyList<LunarDay> LunarDays(SunTrack track, DateTimeOffset fromUtc)
    {
        ArgumentNullException.ThrowIfNull(track);
        var days = new List<LunarDay>();
        var start = Math.Clamp(track.IndexOf(fromUtc), 0, track.Count - 1);
        while (start > 0 && track.Irradiance[start] > 0 && track.Irradiance[start - 1] > 0)
        {
            start--;
        }

        var i = start;
        while (i < track.Count)
        {
            while (i < track.Count && track.Irradiance[i] <= 0)
            {
                i++;
            }

            if (i >= track.Count)
            {
                break;
            }

            var rise = i;
            while (i < track.Count && track.Irradiance[i] > 0)
            {
                i++;
            }

            days.Add(new LunarDay(days.Count + 1, track.At(rise), track.At(i), rise == 0 && track.Irradiance[0] > 0, i >= track.Count));
        }

        return days;
    }

    public static double[] OnShare(SunTrack track, IReadOnlyList<InferenceSample> samples, int first, int stride, int maxSamples = 400)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(samples);
        var picked = Thin(samples, maxSamples).Select(s => (Normal: LanderGeometry.Direction(s.AzimuthDeg, s.ElevationDeg), s.Threshold)).ToList();
        var n = (track.Count - first + stride - 1) / stride;
        var share = new double[Math.Max(0, n)];
        if (picked.Count == 0)
        {
            return share;
        }

        for (var k = 0; k < share.Length; k++)
        {
            var i = first + k * stride;
            var on = 0;
            foreach (var (normal, threshold) in picked)
            {
                on += AttitudeInference.IsOn(track, i, normal, threshold) ? 1 : 0;
            }

            share[k] = (double)on / picked.Count;
        }

        return share;
    }

    public static IReadOnlyList<DayPrediction> PredictDays(SunTrack track, IReadOnlyList<InferenceSample> samples, IReadOnlyList<LunarDay> days, DateTimeOffset landingUtc, int maxSamples = 600)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(days);
        var picked = Thin(samples, maxSamples).Select(s => (Normal: LanderGeometry.Direction(s.AzimuthDeg, s.ElevationDeg), s.Threshold)).ToList();
        var result = new List<DayPrediction>();
        foreach (var day in days)
        {
            var from = Math.Max(track.IndexOf(day.SunriseUtc), track.IndexOf(landingUtc));
            var to = Math.Min(track.Count - 1, track.IndexOf(day.SunsetUtc));
            var firsts = new List<double>();
            var lasts = new List<double>();
            foreach (var (normal, threshold) in picked)
            {
                int? first = null, last = null;
                for (var i = from; i <= to; i++)
                {
                    if (AttitudeInference.IsOn(track, i, normal, threshold))
                    {
                        first ??= i;
                        last = i;
                    }
                }

                if (first is { } f && last is { } l)
                {
                    firsts.Add((track.At(f) - landingUtc).TotalHours);
                    lasts.Add((track.At(l + 1) - landingUtc).TotalHours);
                }
            }

            result.Add(new DayPrediction(day, picked.Count == 0 ? 0 : (double)firsts.Count / picked.Count, AttitudeInference.Quantiles(firsts), AttitudeInference.Quantiles(lasts)));
        }

        return result;
    }

    public static ModelScore Score(SunTrack track, PredictiveModel model, LanderObservation observation)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(observation);
        var rules = model.On is { } single
            ? [single]
            : (model.Samples ?? []).Select(s =>
            {
                var normal = LanderGeometry.Direction(s.AzimuthDeg, s.ElevationDeg);
                return (Func<int, bool>)(i => AttitudeInference.IsOn(track, i, normal, s.Threshold));
            }).ToList();
        if (rules.Count == 0)
        {
            return new ModelScore(model.Id, observation, 0, 1, -Math.Log(LogFloor), null);
        }

        var hits = 0;
        var errors = new List<double>();
        foreach (var rule in rules)
        {
            hits += AttitudeInference.Satisfies(track, observation, rule) ? 1 : 0;
            if (AttitudeInference.ErrorHours(track, observation, rule) is { } e)
            {
                errors.Add(e);
            }
        }

        var p = (double)hits / rules.Count;
        return new ModelScore(model.Id, observation, p, (1 - p) * (1 - p), -Math.Log(Math.Max(LogFloor, p)), AttitudeInference.Quantiles(errors)?.Median);
    }

    public static Func<int, bool> SunUp(SunTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return i => track.Irradiance[i] > 0;
    }

    public static Func<int, bool> FixedPanel(SunTrack track, double azimuthDeg, double elevationDeg, double threshold)
    {
        var normal = LanderGeometry.Direction(azimuthDeg, elevationDeg);
        return i => AttitudeInference.IsOn(track, i, normal, threshold);
    }

    public static IReadOnlyList<T> Thin<T>(IReadOnlyList<T> all, int max)
    {
        ArgumentNullException.ThrowIfNull(all);
        if (all.Count <= max)
        {
            return all;
        }

        var step = (double)all.Count / max;
        return Enumerable.Range(0, max).Select(k => all[(int)(k * step)]).ToList();
    }
}
