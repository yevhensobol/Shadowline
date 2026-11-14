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

    [Fact]
    public void Custom_mission_keeps_its_lander_with_water_weights()
    {
        var mission = Mission.Default with { PanelW = 250, LoadW = 90, BatteryWh = 4000, DurationDays = 90, MinEarthShare = 0.3 };

        var profile = FinderPreset.Profile(mission, null, Day);

        Assert.Equal(60, profile.DurationDays);
        Assert.Equal(new LanderSpec(250, 90, 4000), profile.Lander);
        Assert.Equal(0.3, profile.MinEarthShare);
        Assert.Equal(TargetKind.ColdTrap, profile.Target);
        Assert.Equal(FinderPreset.Water.Weights, profile.Weights);
    }

    [Fact]
    public void Shipped_grid_gives_ranked_zones_that_pass_the_filters()
    {
        var grid = FinderReader.ReadGzip(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "data", "finder.bin.gz")));
        var day = grid.StartUtc.AddDays(120);

        foreach (var preset in FinderPreset.All)
        {
            var result = SiteFinder.Evaluate(grid, FinderPreset.Profile(preset.Apply(Mission.Default), preset, day), FinderPreset.MinZoneCells);

            if (preset == FinderPreset.Water || preset == FinderPreset.Clps)
            {
                Assert.NotEmpty(result.Zones);
            }

            Assert.Equal(Enumerable.Range(1, result.Zones.Count), result.Zones.Select(zone => zone.Rank));
            foreach (var zone in result.Zones.Take(3))
            {
                Assert.True(result.Passes(zone.BestCell));
                Assert.True(zone.MaxScore >= SiteFinder.ZoneScore);
                Assert.True(zone.LatitudeDeg <= grid.DomainLatitudeDeg);
            }
        }
    }
}
