using System.IO.Compression;
using Shadowline.Core.Finder;
using Shadowline.Data;

namespace Shadowline.Tests;

public class FinderTests
{
    private const int Size = 4;
    private const int Steps = 16;

    private static FinderGrid Grid(Func<int, int, bool> sun, Func<int, int, bool>? earth = null, byte slopeHigh = 8)
    {
        var cells = Size * Size;
        var perCell = (Steps + 7) / 8;
        var sunBits = new byte[cells * perCell];
        var earthBits = new byte[cells * perCell];
        for (var cell = 0; cell < cells; cell++)
        {
            for (var step = 0; step < Steps; step++)
            {
                if (sun(cell, step))
                {
                    sunBits[cell * perCell + step / 8] |= (byte)(1 << (step % 8));
                }

                if ((earth ?? ((_, _) => true))(cell, step))
                {
                    earthBits[cell * perCell + step / 8] |= (byte)(1 << (step % 8));
                }
            }
        }

        var full = Enumerable.Repeat((byte)1, cells).ToArray();
        var slope = Enumerable.Repeat(slopeHigh, cells).ToArray();
        var water = Enumerable.Repeat((byte)2, cells).ToArray();
        return new FinderGrid(Size, 2000, Steps, 360, DateTimeOffset.UnixEpoch, -84, full, slope, slope, water, full, full, sunBits, earthBits);
    }

    private static MissionProfile Profile(int durationDays = 2, double maxSlope = 8, double batteryKWh = 10) =>
        new(DateTimeOffset.UnixEpoch, 0, durationDays, new LanderSpec(400, 100, batteryKWh * 1000), maxSlope, 0.5, TargetKind.None, null, null, 5, CriteriaWeights.Default);

    [Fact]
    public void Window_counts_sunlight_and_longest_darkness()
    {
        var grid = Grid((_, step) => step is < 2 or >= 5);

        var window = SiteFinder.Window(grid, 0, 0, 8);

        Assert.Equal(5.0 / 8, window.SunShare, 6);
        Assert.Equal(18.0, window.LongestDarkHours, 6);
    }

    [Fact]
    public void Battery_runs_out_when_each_night_drains_more_than_a_day_charges()
    {
        var grid = Grid((_, step) => step % 4 == 0);
        var profile = Profile(durationDays: 4, batteryKWh: 2.4);

        var result = SiteFinder.Evaluate(grid, profile, 1);

        Assert.True(result.Windows[0].SunShare >= profile.RequiredSunShare);
        Assert.True(result.Windows[0].LongestDarkHours <= profile.MaxDarkHours);
        Assert.Equal(0, result.Windows[0].MinCharge);
        Assert.Equal(FinderFail.Battery, result.Fail[0]);
    }

    [Fact]
    public void Battery_keeps_a_margin_when_days_recharge_it()
    {
        var grid = Grid((_, step) => step % 4 != 3);

        var window = SiteFinder.Window(grid, 0, 0, 16, new LanderSpec(400, 100, 10000));

        Assert.InRange(window.MinCharge, 0.9, 1.0);
    }

    [Fact]
    public void Lander_limits_follow_battery_and_panels()
    {
        var lander = new LanderSpec(400, 100, 10000);

        Assert.Equal(76.0, lander.MaxDarkHours, 6);
        Assert.Equal(0.25, lander.RequiredSunShare, 6);
    }
}
