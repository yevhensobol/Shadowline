using Shadowline.Core.Tracks;
using Shadowline.Core.Windows;

namespace Shadowline.Tests;

public class LandingWindowFinderTests
{
    private static BitTrack Track(string pattern)
    {
        var track = new BitTrack(pattern.Length);
        for (var i = 0; i < pattern.Length; i++)
        {
            track[i] = pattern[i] == '1';
        }

        return track;
    }

    [Fact]
    public void Finds_runs_of_valid_landing_times()
    {
        var sun = Track("0011111111000011111100");
        var link = Track("1111111111111111111111");

        var windows = LandingWindowFinder.Find(sun, link, _ => 5, new WindowCriteria(4, 1.0, 1.0, 0));

        Assert.Equal(2, windows.Count);
        Assert.Equal(2, windows[0].First);
        Assert.Equal(6, windows[0].Last);
        Assert.Equal(8, windows[0].SunStepsAfterLanding);
        Assert.Equal(14, windows[1].First);
        Assert.Equal(16, windows[1].Last);
    }

    [Fact]
    public void Short_breaks_are_merged_and_counted()
    {
        var windows = new[]
        {
            new LandingWindow(0, 4, 2, 0.9, 1, 10, 3),
            new LandingWindow(7, 9, 8, 0.95, 1, 12, 4),
            new LandingWindow(30, 31, 30, 0.8, 1, 5, 2),
        };

        var merged = LandingWindowFinder.Merge(windows, 2);

        Assert.Equal(2, merged.Count);
        Assert.Equal(0, merged[0].First);
        Assert.Equal(9, merged[0].Last);
        Assert.Equal(8, merged[0].Best);
        Assert.Equal(1, merged[0].Breaks);
        Assert.Equal(30, merged[1].First);
    }

    [Fact]
    public void Link_share_and_landing_elevation_filter_candidates()
    {
        var sun = Track("1111111111");
        var link = Track("1100000011");

        var none = LandingWindowFinder.Find(sun, link, _ => 5, new WindowCriteria(4, 0.5, 0.6, 0));
        var low = LandingWindowFinder.Find(sun, Track("1111111111"), i => i < 5 ? 1 : 8, new WindowCriteria(2, 0.5, 0.5, 6));

        Assert.Empty(none);
        var window = Assert.Single(low);
        Assert.Equal(5, window.First);
        Assert.Equal(8, window.Last);
    }
}
