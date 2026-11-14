using Shadowline.Core.Metrics;
using Shadowline.Core.Time;
using Shadowline.Core.Tracks;

namespace Shadowline.Tests;

public class WindowEvaluatorTests
{
    private static readonly TimeGrid Grid = new(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromMinutes(30), 8);

    private static BitTrack Track(string bits)
    {
        var track = new BitTrack(bits.Length);
        for (var i = 0; i < bits.Length; i++)
        {
            track[i] = bits[i] == '1';
        }

        return track;
    }

    [Fact]
    public void Computes_fractions_and_longest_gaps_inside_the_window()
    {
        var tracks = new SkyTracks(Track("11000110"), Track("10111011"), Track("00011000"));

        var metrics = WindowEvaluator.Evaluate(tracks, Grid, 1, 6);

        Assert.Equal(3.0 / 6, metrics.SunFraction);
        Assert.Equal(4.0 / 6, metrics.EarthFraction);
        Assert.Equal(TimeSpan.FromMinutes(90), metrics.LongestDarkness);
        Assert.Equal(TimeSpan.FromMinutes(30), metrics.LongestEarthGap);
        Assert.Equal(TimeSpan.FromMinutes(60), metrics.EarthOverSun);
    }

    [Fact]
    public void Window_outside_the_track_is_rejected()
    {
        var tracks = new SkyTracks(Track("11110000"), Track("11110000"), Track("00000000"));

        Assert.Throws<ArgumentOutOfRangeException>(() => WindowEvaluator.Evaluate(tracks, Grid, 6, 5));
    }

    [Fact]
    public void Non_positive_window_is_rejected()
    {
        var tracks = new SkyTracks(Track("11110000"), Track("11110000"), Track("00000000"));

        Assert.Throws<ArgumentOutOfRangeException>(() => WindowEvaluator.Evaluate(tracks, Grid, 0, 0));
    }
}
