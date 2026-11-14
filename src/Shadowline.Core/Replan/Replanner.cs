using Shadowline.Core.Metrics;
using Shadowline.Core.Sites;

namespace Shadowline.Core.Replan;

public sealed class Replanner : IReplanner
{
    private const double GapFloor = 1e-9;
    private const int HintCount = 3;

    public ReplanResult Plan(ReplanRequest request)
    {
        var preferred = Validate(request);
        var lengthSteps = (request.Requirements.Duration.Ticks - 1) / request.Grid.Step.Ticks + 1;
        var delayCount = request.MaxDelay.Ticks / request.StartStep.Ticks + 1;

        var candidates = new List<Candidate>();
        foreach (var entry in request.Sites)
        {
            var distanceKm = SiteDistance.Km(preferred, entry.Site);
            for (var k = 0L; k < delayCount; k++)
            {
                var start = request.PlannedStart + TimeSpan.FromTicks(request.StartStep.Ticks * k);
                candidates.Add(Build(request, entry, start, lengthSteps, distanceKm));
            }
        }

        var variants = ParetoFront(candidates.Where(c => c.Accepted));

        var rejected = candidates
            .Where(c => !c.Accepted)
            .OrderBy(c => c.Violations.Count)
            .ThenBy(LargestRelativeGap)
            .ThenBy(c => c.Delay)
            .ThenBy(c => c.Site.Id, StringComparer.Ordinal)
            .ToArray();

        var hints = rejected
            .Where(c => !c.Violations.Any(v => v.Kind == RequirementKind.OutsideGrid))
            .Take(HintCount)
            .Select(c => new RelaxHint(
                c,
                c.Violations.Select(v => new RequirementAdjustment(v.Kind, v.Required, v.Actual)).ToArray()))
            .ToArray();

        return new ReplanResult(variants, rejected, hints);
    }

    private static Site Validate(ReplanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Grid);
        ArgumentNullException.ThrowIfNull(request.Sites);
        ArgumentNullException.ThrowIfNull(request.PreferredSiteId);
        ArgumentNullException.ThrowIfNull(request.Requirements);

        Site? preferred = null;
        foreach (var entry in request.Sites)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(entry.Site);
            ArgumentNullException.ThrowIfNull(entry.Tracks);
            if (preferred is null && string.Equals(entry.Site.Id, request.PreferredSiteId, StringComparison.Ordinal))
            {
                preferred = entry.Site;
            }
        }

        if (preferred is null)
        {
            throw new ArgumentException("Preferred site is not in the site list", nameof(request));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(request.StartStep, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.MaxDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(request.Requirements.Duration, TimeSpan.Zero);

        return preferred;
    }

    private static Candidate Build(ReplanRequest request, SiteTracks entry, DateTimeOffset start, long lengthSteps, double distanceKm)
    {
        var delay = start - request.PlannedStart;
        var startIndex = request.Grid.IndexOf(start);
        if (startIndex < 0 || startIndex + lengthSteps > request.Grid.Count)
        {
            return new Candidate(entry.Site, start, delay, distanceKm, null, [new Violation(RequirementKind.OutsideGrid, 0, 0)]);
        }

        var metrics = WindowEvaluator.Evaluate(entry.Tracks, request.Grid, startIndex, (int)lengthSteps);
        return new Candidate(entry.Site, start, delay, distanceKm, metrics, Check(metrics, request.Requirements));
    }

    private static Violation[] Check(WindowMetrics metrics, MissionRequirements requirements)
    {
        var violations = new List<Violation>(4);
        if (metrics.SunFraction < requirements.MinSunFraction)
        {
            violations.Add(new Violation(RequirementKind.SunFraction, requirements.MinSunFraction, metrics.SunFraction));
        }

        if (metrics.LongestDarkness > requirements.MaxDarkness)
        {
            violations.Add(new Violation(RequirementKind.Darkness, requirements.MaxDarkness.TotalHours, metrics.LongestDarkness.TotalHours));
        }

        if (metrics.EarthFraction < requirements.MinEarthFraction)
        {
            violations.Add(new Violation(RequirementKind.EarthFraction, requirements.MinEarthFraction, metrics.EarthFraction));
        }

        if (metrics.LongestEarthGap > requirements.MaxEarthGap)
        {
            violations.Add(new Violation(RequirementKind.EarthGap, requirements.MaxEarthGap.TotalHours, metrics.LongestEarthGap.TotalHours));
        }

        return violations.ToArray();
    }

    private static Candidate[] ParetoFront(IEnumerable<Candidate> accepted)
    {
        var sorted = accepted
            .OrderBy(c => c.Delay)
            .ThenBy(c => c.DistanceKm)
            .ThenBy(c => c.Site.Id, StringComparer.Ordinal)
            .ToArray();

        var front = new List<Candidate>();
        var bestEarlierDistance = double.PositiveInfinity;
        var i = 0;
        while (i < sorted.Length)
        {
            var delay = sorted[i].Delay;
            var groupDistance = sorted[i].DistanceKm;
            for (; i < sorted.Length && sorted[i].Delay == delay; i++)
            {
                if (sorted[i].DistanceKm == groupDistance && groupDistance < bestEarlierDistance)
                {
                    front.Add(sorted[i]);
                }
            }

            bestEarlierDistance = Math.Min(bestEarlierDistance, groupDistance);
        }

        return front.ToArray();
    }

    private static double LargestRelativeGap(Candidate candidate)
    {
        var largest = 0.0;
        foreach (var violation in candidate.Violations)
        {
            var gap = Math.Abs(violation.Actual - violation.Required) / Math.Max(violation.Required, GapFloor);
            largest = Math.Max(largest, gap);
        }

        return largest;
    }
}
