using Shadowline.Core.Metrics;
using Shadowline.Core.Sites;

namespace Shadowline.Core.Replan;

public enum RequirementKind
{
    SunFraction,
    Darkness,
    EarthFraction,
    EarthGap,
    OutsideGrid
}

public sealed record Violation(RequirementKind Kind, double Required, double Actual);

public sealed record Candidate(
    Site Site,
    DateTimeOffset Start,
    TimeSpan Delay,
    double DistanceKm,
    WindowMetrics? Metrics,
    IReadOnlyList<Violation> Violations)
{
    public bool Accepted => Violations.Count == 0;
}

public sealed record RequirementAdjustment(RequirementKind Kind, double From, double To);

public sealed record RelaxHint(Candidate Candidate, IReadOnlyList<RequirementAdjustment> Adjustments);

public sealed record ReplanResult(
    IReadOnlyList<Candidate> Variants,
    IReadOnlyList<Candidate> Rejected,
    IReadOnlyList<RelaxHint> Hints);
