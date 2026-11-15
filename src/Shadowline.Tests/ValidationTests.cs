using Shadowline.Core.Ephemeris;
using Shadowline.Core.Sites;
using Shadowline.Core.Time;
using Shadowline.Core.Validation;

namespace Shadowline.Tests;

public class ValidationTests
{
    private static readonly DateTimeOffset Origin = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeGrid Grid = new(Origin, TimeSpan.FromMinutes(30), 2);
    private static readonly Site Equator = new("eq", "eq", null, 0, 0, 0);

    private static BodyEphemeris Ephemeris() => new(Grid, new float[]
    {
        1.4959787e8f, 0, 0, 3.844e5f, 0, 0,
        0, 1.4959787e8f, 0, 0, 3.844e5f, 0
    });

    private static Site? Find(string id) => id == "eq" ? Equator : null;

    [Fact]
    public void Matching_reference_gives_near_zero_error()
    {
        var fixture = new ValidationFixture("eq", "sun", Origin, 0, 90);

        var result = ValidationRunner.Run([fixture], Find, Ephemeris());

        var row = Assert.Single(result.Rows);
        Assert.True(Math.Abs(row.ElevationErrorDeg) < 0.01);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void Errors_are_computed_against_the_reference()
    {
        var fixture = new ValidationFixture("eq", "earth", Origin, 0, 89.5);

        var row = Assert.Single(ValidationRunner.Run([fixture], Find, Ephemeris()).Rows);

        Assert.True(Math.Abs(row.ElevationErrorDeg - 0.5) < 0.05);
    }

    [Fact]
    public void Azimuth_difference_wraps_around_north()
    {
        Assert.True(Math.Abs(ValidationRunner.AngleDifference(359.9, 0.1) + 0.2) < 1e-9);
        Assert.True(Math.Abs(ValidationRunner.AngleDifference(0.1, 359.9) - 0.2) < 1e-9);
        Assert.True(Math.Abs(ValidationRunner.AngleDifference(10, 350) - 20) < 1e-9);
    }

    [Fact]
    public void Unknown_site_body_or_misaligned_time_is_skipped()
    {
        var fixtures = new[]
        {
            new ValidationFixture("nope", "sun", Origin, 0, 0),
            new ValidationFixture("eq", "mars", Origin, 0, 0),
            new ValidationFixture("eq", "sun", Origin.AddMinutes(10), 0, 0),
            new ValidationFixture("eq", "sun", Origin.AddDays(5), 0, 0)
        };

        var result = ValidationRunner.Run(fixtures, Find, Ephemeris());

        Assert.Empty(result.Rows);
        Assert.Equal(4, result.Skipped);
    }

    [Fact]
    public void Summary_reports_maxima_and_mean()
    {
        var fixtures = new[]
        {
            new ValidationFixture("eq", "earth", Origin, 0, 89.0),
            new ValidationFixture("eq", "earth", Origin.AddMinutes(30), 90, 0.0)
        };

        var summary = ValidationRunner.Run(fixtures, Find, Ephemeris()).Summary();

        Assert.Equal(2, summary.Count);
        Assert.True(summary.MaxElevationErrorDeg >= summary.MeanElevationErrorDeg);
    }

    [Fact]
    public void Empty_run_has_a_zero_summary()
    {
        var summary = ValidationRunner.Run([], Find, Ephemeris()).Summary();

        Assert.Equal(0, summary.Count);
        Assert.Equal(0.0, summary.MaxAzimuthErrorDeg);
    }
}
