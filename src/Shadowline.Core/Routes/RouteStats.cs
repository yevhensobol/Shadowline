namespace Shadowline.Core.Routes;

public sealed record RouteSample(double DistanceKm, double ElevationM, double SlopeDeg, double Sun, double Earth, bool Dark);

public sealed record RouteSummary(
    double LengthKm,
    double ClimbM,
    double DescentM,
    double MaxGradeDeg,
    double SteepFraction,
    double SunMin,
    double SunMean,
    double DarkKm,
    double EarthMean);

public static class RouteStats
{
    public const double SteepDeg = 15.0;

    public const double GradeBaseKm = 0.5;

    public static RouteSummary Summarize(IReadOnlyList<RouteSample> samples)
    {
        if (samples.Count < 2)
        {
            return new RouteSummary(0, 0, 0, 0, 0, samples.Count == 1 ? samples[0].Sun : 0, samples.Count == 1 ? samples[0].Sun : 0, 0, samples.Count == 1 ? samples[0].Earth : 0);
        }

        double climb = 0, descent = 0, steep = 0, dark = 0, sun = 0, earth = 0, weight = 0;
        var sunMin = double.MaxValue;
        for (var i = 1; i < samples.Count; i++)
        {
            var length = Math.Max(0, samples[i].DistanceKm - samples[i - 1].DistanceKm);
            var rise = samples[i].ElevationM - samples[i - 1].ElevationM;
            if (rise > 0)
            {
                climb += rise;
            }
            else
            {
                descent -= rise;
            }

            var mid = samples[i];
            weight += length;
            sun += mid.Sun * length;
            earth += mid.Earth * length;
            if (mid.SlopeDeg >= SteepDeg)
            {
                steep += length;
            }

            if (mid.Dark)
            {
                dark += length;
            }
        }

        foreach (var sample in samples)
        {
            sunMin = Math.Min(sunMin, sample.Sun);
        }

        var total = samples[^1].DistanceKm - samples[0].DistanceKm;
        return new RouteSummary(
            total,
            climb,
            descent,
            MaxGrade(samples),
            weight > 0 ? steep / weight : 0,
            sunMin,
            weight > 0 ? sun / weight : samples[0].Sun,
            dark,
            weight > 0 ? earth / weight : samples[0].Earth);
    }

    public static double MaxGrade(IReadOnlyList<RouteSample> samples)
    {
        var best = 0.0;
        var j = 0;
        for (var i = 0; i < samples.Count; i++)
        {
            j = Math.Max(j, i + 1);
            while (j < samples.Count && samples[j].DistanceKm - samples[i].DistanceKm < GradeBaseKm)
            {
                j++;
            }

            if (j >= samples.Count)
            {
                break;
            }

            var run = (samples[j].DistanceKm - samples[i].DistanceKm) * 1000;
            var grade = Math.Atan(Math.Abs(samples[j].ElevationM - samples[i].ElevationM) / run) * 180 / Math.PI;
            best = Math.Max(best, grade);
        }

        return best;
    }
}
