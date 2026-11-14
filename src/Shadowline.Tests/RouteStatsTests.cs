using Shadowline.Core.Routes;

namespace Shadowline.Tests;

public class RouteStatsTests
{
    [Fact]
    public void Sums_climb_descent_and_weights_by_length()
    {
        var samples = new[]
        {
            new RouteSample(0, 100, 2, 0.8, 0.5, false),
            new RouteSample(1, 150, 4, 0.6, 0.5, false),
            new RouteSample(2, 120, 20, 0.0, 0.0, true),
            new RouteSample(4, 120, 3, 0.4, 1.0, false),
        };

        var summary = RouteStats.Summarize(samples);

        Assert.True(Math.Abs((4) - (summary.LengthKm)) < 1e-6);
        Assert.True(Math.Abs((50) - (summary.ClimbM)) < 1e-6);
        Assert.True(Math.Abs((30) - (summary.DescentM)) < 1e-6);
        Assert.True(Math.Abs((0.25) - (summary.SteepFraction)) < 1e-6);
        Assert.True(Math.Abs((1) - (summary.DarkKm)) < 1e-6);
        Assert.True(Math.Abs((0.0) - (summary.SunMin)) < 1e-6);
        Assert.True(Math.Abs(((0.6 + 0.0 + 0.4 * 2) / 4) - (summary.SunMean)) < 1e-6);
        Assert.True(Math.Abs(((0.5 + 0.0 + 1.0 * 2) / 4) - (summary.EarthMean)) < 1e-6);
    }

    [Fact]
    public void Grade_uses_a_half_kilometre_base()
    {
        var samples = new[]
        {
            new RouteSample(0, 0, 0, 1, 1, false),
            new RouteSample(0.1, 30, 0, 1, 1, false),
            new RouteSample(0.5, 0, 0, 1, 1, false),
            new RouteSample(1.0, 500 * Math.Tan(Math.PI / 18), 0, 1, 1, false),
        };

        Assert.True(Math.Abs((10) - (RouteStats.MaxGrade(samples))) < 1e-6);
    }

    [Fact]
    public void Single_point_has_no_length()
    {
        var summary = RouteStats.Summarize([new RouteSample(0, 10, 3, 0.7, 0.2, false)]);

        Assert.Equal(0, summary.LengthKm);
        Assert.Equal(0.7, summary.SunMean);
    }
}
