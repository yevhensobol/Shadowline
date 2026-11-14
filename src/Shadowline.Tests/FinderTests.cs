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

    [Fact]
    public void Steep_cells_fail_the_slope_filter()
    {
        var grid = Grid((_, _) => true, slopeHigh: 60);

        var result = SiteFinder.Evaluate(grid, Profile(maxSlope: 8), 1);

        Assert.All(result.Fail, fail => Assert.True(fail.HasFlag(FinderFail.Slope)));
        Assert.Empty(result.Zones);
    }

    [Fact]
    public void Long_night_fails_the_darkness_filter()
    {
        var grid = Grid((_, step) => step == 0);

        var result = SiteFinder.Evaluate(grid, Profile(durationDays: 4, batteryKWh: 1), 1);

        Assert.True(result.Fail[0].HasFlag(FinderFail.Darkness));
    }

    [Fact]
    public void Connected_passing_cells_form_one_zone()
    {
        var grid = Grid((_, _) => true);

        var result = SiteFinder.Evaluate(grid, Profile(), 1);

        Assert.Single(result.Zones);
        Assert.Equal(Size * Size, result.Zones[0].Cells.Count);
        Assert.Equal(Size * Size * 4.0, result.Zones[0].AreaKm2, 6);
    }

    [Fact]
    public void Gzip_file_round_trips()
    {
        var grid = Grid((cell, step) => (cell + step) % 3 == 0);
        var bytes = new List<byte>();
        bytes.AddRange("SLFD"u8.ToArray());
        bytes.AddRange(BitConverter.GetBytes(1));
        bytes.AddRange(BitConverter.GetBytes(Size));
        bytes.AddRange(BitConverter.GetBytes(2000f));
        bytes.AddRange(BitConverter.GetBytes(4000f));
        bytes.AddRange(BitConverter.GetBytes(Steps));
        bytes.AddRange(BitConverter.GetBytes(360));
        bytes.AddRange(BitConverter.GetBytes(0L));
        bytes.AddRange(BitConverter.GetBytes(-84f));
        foreach (var plane in new[] { grid.Domain, grid.SlopeMedian, grid.SlopeHigh, grid.WaterKm, grid.SunYear, grid.EarthYear })
        {
            bytes.AddRange(plane);
        }

        for (var cell = 0; cell < grid.Count; cell++)
        {
            var row = new byte[grid.BytesPerCell];
            for (var step = 0; step < Steps; step++)
            {
                if (grid.Sun(cell, step))
                {
                    row[step / 8] |= (byte)(1 << (step % 8));
                }
            }

            bytes.AddRange(row);
        }

        bytes.AddRange(new byte[grid.Count * grid.BytesPerCell]);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true))
        {
            gzip.Write(bytes.ToArray());
        }

        var read = FinderReader.ReadGzip(output.ToArray());

        Assert.Equal(Size, read.Size);
        Assert.Equal(grid.Sun(5, 1), read.Sun(5, 1));
        Assert.False(read.Earth(5, 1));
    }

    [Fact]
    public void Polar_coordinates_round_trip()
    {
        var (x, y) = FinderGrid.ToXY(-86.5, 120);
        var (lat, lon) = FinderGrid.ToLatLon(x, y);

        Assert.Equal(-86.5, lat, 9);
        Assert.Equal(120, lon, 9);
    }
}
