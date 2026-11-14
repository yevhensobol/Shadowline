using Shadowline.Core.Missions;

namespace Shadowline.Tests;

public class TouchdownStabilityTests
{
    [Fact]
    public void Flat_ground_tip_speed_matches_blanco_and_mungan_for_im1()
    {
        var speed = TouchdownStability.TipSpeed(LanderStance.NovaC, 0, 0, 0);

        Assert.InRange(speed, 2.3, 2.45);
    }

    [Fact]
    public void Slope_beyond_the_static_angle_tips_without_speed()
    {
        var stance = LanderStance.NovaC;

        Assert.Equal(0, TouchdownStability.TipSpeed(stance, stance.StaticTipDeg + 1, 0, 30));
        Assert.True(TouchdownStability.TipSpeed(stance, stance.StaticTipDeg - 1, 0, 30) > 0);
        Assert.True(TouchdownStability.TipSpeed(stance, 10, 180, 30) > TouchdownStability.TipSpeed(stance, 10, 0, 30));
    }

    [Fact]
    public void Apollo_lm_is_stable_at_the_design_slope_and_lateral_speed()
    {
        Assert.True(TouchdownStability.WorstTipSpeed(LanderStance.ApolloLm, 12) > 1.22);
    }

    [Fact]
    public void Im1_on_twelve_degrees_at_one_metre_per_second_is_near_the_boundary()
    {
        var worst = TouchdownStability.WorstTipSpeed(LanderStance.NovaC, 12);

        Assert.InRange(worst, 0.9, 1.5);
    }

    [Fact]
    public void Probability_grows_with_slope_and_speed()
    {
        var flat = TouchdownStability.TipProbability(LanderStance.NovaC, [new(0, 1)], 1.0);
        var steep = TouchdownStability.TipProbability(LanderStance.NovaC, [new(12, 1)], 1.0);
        var faster = TouchdownStability.TipProbability(LanderStance.NovaC, [new(12, 1)], 2.0);

        Assert.Equal(0, flat);
        Assert.True(steep > flat);
        Assert.True(faster > steep);
        Assert.Equal(1, TouchdownStability.TipProbability(LanderStance.NovaC, [new(45, 1)], 0));
    }

    [Fact]
    public void Hurst_scaling_steepens_slopes_below_the_dem_baseline()
    {
        Assert.True(TouchdownStability.FootprintSlopeDeg(10, 160, 4.4) > 10);
        Assert.Equal(10, TouchdownStability.FootprintSlopeDeg(10, 4, 4.4));
        Assert.Equal(10, TouchdownStability.FootprintSlopeDeg(10, 160, 4.4, 1.0), 9);
    }

    [Fact]
    public void Mission_link_keeps_the_stance_and_lateral_speed()
    {
        var mission = Mission.Default with { StanceId = "apollo-lm", LateralSpeedMs = 1.22 };

        var parsed = Mission.ParseQueryValue(mission.ToQueryValue());

        Assert.Equal("apollo-lm", parsed!.StanceId);
        Assert.Equal(1.22, parsed.LateralSpeedMs, 6);
        Assert.Equal("nova-c", Mission.ParseQueryValue("400,150,10,14,50")!.StanceId);
        Assert.Null(Mission.ParseQueryValue("400,150,10,14,50,100,unknown,1"));
    }
}

public class LandingRecordsTests
{
    [Fact]
    public void Every_landing_record_has_a_reference_and_they_are_sorted()
    {
        var misses = Shadowline.Core.Missions.LandingRecords.Misses;

        Assert.All(misses, item => Assert.NotNull(Shadowline.Core.Reports.References.Find(item.ReferenceId)));
        Assert.Equal(misses.OrderBy(item => item.DistanceM).Select(item => item.Key), misses.Select(item => item.Key));
        Assert.Equal(["slim", "blueghost1", "apollo12", "im2"], Shadowline.Core.Missions.LandingRecords.Within(1000).Select(item => item.Key));
    }
}
