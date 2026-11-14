using Shadowline.Core.Finder;
using Shadowline.Core.Missions;
using Shadowline.Core.Power;

namespace Shadowline.Tests;

public class MissionTests
{
    [Fact]
    public void Lander_matches_the_finder_default()
    {
        Assert.Equal(LanderSpec.Default, Mission.Default.Lander);
    }

    [Fact]
    public void Requirements_follow_the_lander()
    {
        var mission = new Mission(400, 150, 10000, 14, 0.5);
        var requirements = mission.Requirements();

        Assert.Equal(TimeSpan.FromDays(14), requirements.Duration);
        Assert.Equal(0.375, requirements.MinSunFraction, 6);
        Assert.Equal(10000 * 0.8 * 0.95 / 150, requirements.MaxDarkness.TotalHours, 6);
        Assert.Equal(0.5, requirements.MinEarthFraction, 6);
    }

    [Fact]
    public void Window_criteria_count_grid_steps()
    {
        var criteria = new Mission(400, 150, 10000, 7, 0.8).Criteria(TimeSpan.FromMinutes(30));

        Assert.Equal(7 * 48, criteria.MissionSteps);
        Assert.Equal(0.375, criteria.MinSunShare, 6);
        Assert.Equal(0.8, criteria.MinLinkShare, 6);
    }

    [Fact]
    public void Tip_options_give_the_same_rated_power_and_usable_battery()
    {
        var mission = new Mission(400, 150, 10000, 14, 0.5);
        var options = mission.TipOptions();

        var rated = options.TotalAreaM2 * PowerModel.SolarConstantW * options.Efficiency * options.SystemFactor;
        Assert.Equal(400, rated, 6);
        Assert.Equal(100, options.LoadW, 6);
        Assert.Equal(mission.SafeModeHours, options.BatteryHours, 6);
        Assert.Equal(10000 * 0.8 * 0.95 / 100, mission.SafeModeHours, 6);
    }

    [Fact]
    public void Query_value_round_trips()
    {
        var mission = new Mission(650, 210, 12500, 21, 0.35, 80);

        var parsed = Mission.ParseQueryValue(mission.ToQueryValue());

        Assert.Equal(mission, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("400,150,10")]
    [InlineData("400,150,10,14,50,100,7")]
    [InlineData("400,abc,10,14,50")]
    [InlineData("400,150,NaN,14,50")]
    public void Bad_query_values_give_no_mission(string? value)
    {
        Assert.Null(Mission.ParseQueryValue(value));
    }

    [Fact]
    public void Five_value_links_keep_the_default_safe_load()
    {
        Assert.Equal(Mission.Default.SafeLoadW, Mission.ParseQueryValue("400,150,10,14,50")!.SafeLoadW);
    }
}
