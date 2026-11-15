using Shadowline.Core.Replan;
using Shadowline.Core.Sites;
using Shadowline.Core.Time;
using Shadowline.Core.Tracks;

namespace Shadowline.Tests;

public class ReplannerTests
{
    private const string Ones = "111111111111";
    private const string Zeros = "000000000000";

    private static readonly DateTimeOffset Origin = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeGrid Grid = new(Origin, TimeSpan.FromHours(1), 12);
    private static readonly MissionRequirements Requirements = new(
        TimeSpan.FromHours(4),
        0.75,
        TimeSpan.FromHours(1),
        0.5,
        TimeSpan.FromHours(2));

    [Fact]
    public void Preferred_site_is_accepted_at_zero_delay()
    {
        var result = Plan(Request(Hours(0), Entry("A", 0, Ones, Ones)));

        var variant = Assert.Single(result.Variants);
        Assert.Equal("A", variant.Site.Id);
        Assert.Equal(Origin, variant.Start);
        Assert.Equal(TimeSpan.Zero, variant.Delay);
        Assert.Equal(0.0, variant.DistanceKm);
        Assert.True(variant.Accepted);
        Assert.Empty(variant.Violations);
        Assert.Equal(1.0, variant.Metrics!.SunFraction);
        Assert.Equal(1.0, variant.Metrics.EarthFraction);
        Assert.Empty(result.Rejected);
        Assert.Empty(result.Hints);
    }

    [Fact]
    public void Later_start_is_accepted_when_the_first_fails()
    {
        var result = Plan(Request(Hours(2), Entry("A", 0, "001111111111", Ones)));

        var variant = Assert.Single(result.Variants);
        Assert.Equal(Hours(1), variant.Delay);
        Assert.Equal(Origin + Hours(1), variant.Start);
        var rejected = Assert.Single(result.Rejected);
        Assert.Equal(TimeSpan.Zero, rejected.Delay);
        Assert.Equal(Origin, rejected.Start);
    }

    [Fact]
    public void Farther_site_at_zero_delay_and_waiting_on_the_preferred_site_are_both_kept()
    {
        var preferred = Entry("A", 0, "001111111111", Ones);
        var farther = Entry("B", 10, Ones, Ones);

        var result = Plan(Request(Hours(2), preferred, farther));

        Assert.Equal(new[] { "B@0", "A@1" }, Keys(result.Variants));
        Assert.Equal(SiteDistance.Km(preferred.Site, farther.Site), result.Variants[0].DistanceKm);
        Assert.True(result.Variants[0].DistanceKm > 0);
        Assert.Equal(0.0, result.Variants[1].DistanceKm);
    }

    [Fact]
    public void Dominated_candidates_are_absent_from_variants()
    {
        var result = Plan(Request(
            Hours(2),
            Entry("A", 0, "000111111111", Ones),
            Entry("B", 10, "001111111111", Ones),
            Entry("C", 20, Ones, Ones)));

        Assert.Equal(new[] { "C@0", "B@1", "A@2" }, Keys(result.Variants));
        Assert.Equal(new[] { "B@0", "A@1", "A@0" }, Keys(result.Rejected));
    }

    [Fact]
    public void Zero_delay_on_the_preferred_site_dominates_every_other_accepted_candidate()
    {
        var result = Plan(Request(
            Hours(2),
            Entry("A", 0, Ones, Ones),
            Entry("B", 10, Ones, Ones),
            Entry("C", 20, Ones, Ones)));

        var variant = Assert.Single(result.Variants);
        Assert.Equal("A@0", Key(variant));
        Assert.Empty(result.Rejected);
    }

    [Fact]
    public void Every_violation_kind_is_reported_in_order_with_required_and_actual_values()
    {
        var result = Plan(Request(Hours(0), Entry("A", 0, "100011111111", "000111111111")));

        Assert.Empty(result.Variants);
        var rejected = Assert.Single(result.Rejected);
        Assert.False(rejected.Accepted);
        Assert.Equal(
            new[]
            {
                new Violation(RequirementKind.SunFraction, 0.75, 0.25),
                new Violation(RequirementKind.Darkness, 1.0, 3.0),
                new Violation(RequirementKind.EarthFraction, 0.5, 0.25),
                new Violation(RequirementKind.EarthGap, 2.0, 3.0),
            },
            rejected.Violations.ToArray());
    }

    [Fact]
    public void Duration_violations_are_reported_in_hours()
    {
        var grid = new TimeGrid(Origin, TimeSpan.FromMinutes(30), 12);
        var requirements = Requirements with { MaxDarkness = TimeSpan.FromMinutes(30), MaxEarthGap = TimeSpan.FromMinutes(30) };
        var request = Request(Hours(0), Entry("A", 0, "101000111111", "110000011111")) with { Grid = grid, Requirements = requirements };

        var rejected = Assert.Single(Plan(request).Rejected);

        Assert.Equal(
            new[]
            {
                new Violation(RequirementKind.SunFraction, 0.75, 0.5),
                new Violation(RequirementKind.Darkness, 0.5, 1.5),
                new Violation(RequirementKind.EarthFraction, 0.5, 0.375),
                new Violation(RequirementKind.EarthGap, 0.5, 2.5),
            },
            rejected.Violations.ToArray());
    }

    [Fact]
    public void Values_equal_to_the_limits_are_accepted()
    {
        var result = Plan(Request(Hours(0), Entry("A", 0, "011111111111", "001111111111")));

        var variant = Assert.Single(result.Variants);
        Assert.Equal(0.75, variant.Metrics!.SunFraction);
        Assert.Equal(Hours(1), variant.Metrics.LongestDarkness);
        Assert.Equal(0.5, variant.Metrics.EarthFraction);
        Assert.Equal(Hours(2), variant.Metrics.LongestEarthGap);
        Assert.Empty(result.Rejected);
    }

    [Fact]
    public void Outside_grid_is_reported_at_both_ends_and_not_used_for_hints()
    {
        var grid = new TimeGrid(Origin, TimeSpan.FromHours(1), 7);
        var request = Request(Hours(5), Entry("A", 0, "0000000", "1111111")) with
        {
            Grid = grid,
            PlannedStart = Origin - Hours(1),
        };

        var result = Plan(request);

        Assert.Empty(result.Variants);
        Assert.Equal(new[] { "A@0", "A@5", "A@1", "A@2", "A@3", "A@4" }, Keys(result.Rejected));

        var before = result.Rejected[0];
        Assert.Equal(Origin - Hours(1), before.Start);
        Assert.True(before.Metrics is null);
        Assert.Equal(new Violation(RequirementKind.OutsideGrid, 0, 0), Assert.Single(before.Violations));

        var after = result.Rejected[1];
        Assert.Equal(Origin + Hours(4), after.Start);
        Assert.True(after.Metrics is null);
        Assert.Equal(new Violation(RequirementKind.OutsideGrid, 0, 0), Assert.Single(after.Violations));

        var lastInside = result.Rejected[5];
        Assert.Equal(Origin + Hours(3), lastInside.Start);
        Assert.False(lastInside.Metrics is null);

        Assert.Equal(new[] { "A@1", "A@2", "A@3" }, result.Hints.Select(h => Key(h.Candidate)).ToArray());
        Assert.True(result.Hints.All(h => h.Adjustments.All(a => a.Kind != RequirementKind.OutsideGrid)));
    }

    [Fact]
    public void No_hints_when_every_rejection_is_outside_the_grid()
    {
        var request = Request(Hours(0), Entry("A", 0, Ones, Ones)) with { PlannedStart = Origin - Hours(1) };

        var result = Plan(request);

        var rejected = Assert.Single(result.Rejected);
        Assert.Equal(RequirementKind.OutsideGrid, Assert.Single(rejected.Violations).Kind);
        Assert.Empty(result.Hints);
    }

    [Fact]
    public void Hint_adjustments_equal_the_violation_values()
    {
        var result = Plan(Request(Hours(0), Entry("A", 0, "100011111111", "000111111111")));

        var rejected = Assert.Single(result.Rejected);
        var hint = Assert.Single(result.Hints);
        Assert.True(ReferenceEquals(rejected, hint.Candidate));
        Assert.Equal(
            new[]
            {
                new RequirementAdjustment(RequirementKind.SunFraction, 0.75, 0.25),
                new RequirementAdjustment(RequirementKind.Darkness, 1.0, 3.0),
                new RequirementAdjustment(RequirementKind.EarthFraction, 0.5, 0.25),
                new RequirementAdjustment(RequirementKind.EarthGap, 2.0, 3.0),
            },
            hint.Adjustments.ToArray());
    }

    [Fact]
    public void Rejected_are_ordered_by_count_then_gap_then_delay_then_site_id()
    {
        var request = Request(
            Hours(1),
            Entry("D", 5, "101010101010", Ones),
            Entry("B", 10, Ones, "010000000000"),
            Entry("A", 0, Zeros, Ones),
            Entry("C", 20, "101010101010", Ones));

        var first = Plan(request);
        var second = Plan(request);

        var expected = new[] { "C@0", "D@0", "C@1", "D@1", "B@0", "B@1", "A@0", "A@1" };
        Assert.Empty(first.Variants);
        Assert.Equal(expected, Keys(first.Rejected));
        Assert.Equal(expected, Keys(second.Rejected));
        Assert.Equal(new[] { "C@0", "D@0", "C@1" }, first.Hints.Select(h => Key(h.Candidate)).ToArray());
    }

    [Fact]
    public void Variants_are_ordered_by_delay_then_distance_then_site_id()
    {
        var request = Request(
            Hours(1),
            Entry("C", 5, "001111111111", Ones),
            Entry("B2", 10, Ones, Ones),
            Entry("A", 0, Zeros, Ones),
            Entry("B1", 10, Ones, Ones));

        var first = Plan(request);
        var second = Plan(request);

        var expected = new[] { "B1@0", "B2@0", "C@1" };
        Assert.Equal(expected, Keys(first.Variants));
        Assert.Equal(expected, Keys(second.Variants));
        Assert.Equal(new[] { "C@0", "A@0", "A@1" }, Keys(first.Rejected));
    }

    [Fact]
    public void Delays_step_from_zero_up_to_max_delay_inclusive()
    {
        var result = Plan(Request(Hours(2.5), Entry("A", 0, Zeros, Ones)));

        Assert.Equal(new[] { "A@0", "A@1", "A@2" }, Keys(result.Rejected));
        Assert.Equal(new[] { Origin, Origin + Hours(1), Origin + Hours(2) }, result.Rejected.Select(c => c.Start).ToArray());
    }

    [Fact]
    public void Duration_is_rounded_up_to_whole_grid_steps()
    {
        var request = Request(Hours(0), Entry("A", 0, "111011111111", Ones)) with
        {
            Requirements = Requirements with { Duration = TimeSpan.FromHours(3.5) },
        };

        var variant = Assert.Single(Plan(request).Variants);

        Assert.Equal(0.75, variant.Metrics!.SunFraction);
        Assert.Equal(Hours(1), variant.Metrics.LongestDarkness);
    }

    [Fact]
    public void Unknown_preferred_site_is_rejected()
    {
        var request = Request(Hours(0), Entry("A", 0, Ones, Ones)) with { PreferredSiteId = "Z" };

        Assert.Throws<ArgumentException>(() => Plan(request));
    }

    [Fact]
    public void Empty_site_list_is_rejected()
    {
        var request = Request(Hours(0));

        Assert.Throws<ArgumentException>(() => Plan(request));
    }

    [Fact]
    public void Non_positive_start_step_is_rejected()
    {
        var zero = Request(Hours(1), Entry("A", 0, Ones, Ones)) with { StartStep = TimeSpan.Zero };
        var negative = Request(Hours(1), Entry("A", 0, Ones, Ones)) with { StartStep = -Hours(1) };

        Assert.Throws<ArgumentOutOfRangeException>(() => Plan(zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan(negative));
    }

    [Fact]
    public void Negative_max_delay_is_rejected()
    {
        var request = Request(-Hours(1), Entry("A", 0, Ones, Ones));

        Assert.Throws<ArgumentOutOfRangeException>(() => Plan(request));
    }

    [Fact]
    public void Non_positive_duration_is_rejected()
    {
        var zero = Request(Hours(0), Entry("A", 0, Ones, Ones)) with
        {
            Requirements = Requirements with { Duration = TimeSpan.Zero },
        };
        var negative = Request(Hours(0), Entry("A", 0, Ones, Ones)) with
        {
            Requirements = Requirements with { Duration = -Hours(1) },
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => Plan(zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan(negative));
    }

    [Fact]
    public void Null_inputs_are_rejected()
    {
        var valid = Request(Hours(0), Entry("A", 0, Ones, Ones));

        Assert.Throws<ArgumentNullException>(() => Plan(null!));
        Assert.Throws<ArgumentNullException>(() => Plan(valid with { Grid = null! }));
        Assert.Throws<ArgumentNullException>(() => Plan(valid with { Sites = null! }));
        Assert.Throws<ArgumentNullException>(() => Plan(valid with { PreferredSiteId = null! }));
        Assert.Throws<ArgumentNullException>(() => Plan(valid with { Requirements = null! }));
        Assert.Throws<ArgumentNullException>(() => Plan(valid with { Sites = new SiteTracks[] { null! } }));
    }

    private static ReplanResult Plan(ReplanRequest request) => new Replanner().Plan(request);

    private static ReplanRequest Request(TimeSpan maxDelay, params SiteTracks[] sites) =>
        new(Grid, sites, "A", Origin, Requirements, maxDelay, Hours(1));

    private static SiteTracks Entry(string id, double longitudeDeg, string sun, string earth) =>
        new(new Site(id, id, null, 0, longitudeDeg, 0), new SkyTracks(Track(sun), Track(earth), new BitTrack(sun.Length)));

    private static BitTrack Track(string pattern)
    {
        var track = new BitTrack(pattern.Length);
        for (var i = 0; i < pattern.Length; i++)
        {
            track[i] = pattern[i] == '1';
        }

        return track;
    }

    private static TimeSpan Hours(double hours) => TimeSpan.FromHours(hours);

    private static string Key(Candidate candidate) => $"{candidate.Site.Id}@{candidate.Delay.TotalHours}";

    private static string[] Keys(IEnumerable<Candidate> candidates) => candidates.Select(Key).ToArray();
}
