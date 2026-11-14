using Shadowline.Core.Finder;
using Shadowline.Core.Missions;
using Shadowline.Data;

namespace Shadowline.Tests;

public class FinderPresetTests
{
    private static readonly DateTimeOffset Day = new(2027, 3, 15, 7, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Keys_are_unique_and_unknown_key_falls_back_to_water()
    {
        Assert.Equal(FinderPreset.All.Count, FinderPreset.All.Select(preset => preset.Key).Distinct().Count());
        Assert.DoesNotContain(FinderPreset.All, preset => preset.Key == FinderPreset.Custom);
        Assert.Same(FinderPreset.Water, FinderPreset.Get("unknown"));
    }

    [Fact]
    public void Applied_preset_is_matched_back()
    {
        foreach (var preset in FinderPreset.All)
        {
            var mission = preset.Apply(Mission.Default with { StanceId = "apollo-lm", SafeLoadW = 60 });
            var stance = preset.StanceId ?? "apollo-lm";

            Assert.Same(preset, FinderPreset.Match(mission));
            Assert.Equal(stance, mission.StanceId);
            Assert.Equal(60, mission.SafeLoadW);
        }
    }

    [Fact]
    public void Edited_mission_is_custom()
    {
        var water = FinderPreset.Water.Apply(Mission.Default);

        Assert.Same(FinderPreset.Water, FinderPreset.Match(water));
        Assert.Null(FinderPreset.Match(water with { BatteryWh = 7000 }));
    }

    [Fact]
    public void Nova_c_presets_follow_the_published_lander()
    {
        foreach (var preset in new[] { FinderPreset.Water, FinderPreset.Clps })
        {
            var mission = preset.Apply(Mission.Default);

            Assert.Equal(FinderPreset.NovaC, preset.LanderKey);
            Assert.Equal(788, mission.PanelW);
            Assert.Equal(1554, mission.BatteryWh);
            Assert.Equal("nova-c", mission.StanceId);
            Assert.True(mission.SafeLoadW <= mission.LoadW);
        }
    }

    [Fact]
    public void Profile_uses_preset_weights_and_mission_lander()
    {
        var mission = FinderPreset.Relay.Apply(Mission.Default);

        var profile = FinderPreset.Profile(mission, FinderPreset.Relay, Day);

        Assert.Equal(new DateTimeOffset(2027, 3, 15, 12, 0, 0, TimeSpan.Zero), profile.LandingUtc);
        Assert.Equal(FinderPreset.FlexDays, profile.FlexDays);
        Assert.Equal(28, profile.DurationDays);
        Assert.Equal(FinderPreset.MaxSlopeDeg, profile.MaxSlopeDeg);
        Assert.Equal(0.75, profile.MinEarthShare);
        Assert.Equal(TargetKind.None, profile.Target);
        Assert.Equal(FinderPreset.Relay.Weights, profile.Weights);
        Assert.Equal(mission.Lander, profile.Lander);
    }
}
