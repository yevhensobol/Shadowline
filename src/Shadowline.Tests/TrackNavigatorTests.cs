using Shadowline.Core.Tracks;

namespace Shadowline.Tests;

public class TrackNavigatorTests
{
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
    public void Finds_the_next_rise_and_set()
    {
        var track = Track("0011100110");

        Assert.Equal(2, TrackNavigator.Find(track, 0, true, true));
        Assert.Equal(5, TrackNavigator.Find(track, 2, true, false));
        Assert.Equal(7, TrackNavigator.Find(track, 5, true, true));
        Assert.Null(TrackNavigator.Find(track, 7, true, true));
    }

    [Fact]
    public void Finds_the_previous_rise_and_set()
    {
        var track = Track("0011100110");

        Assert.Equal(7, TrackNavigator.Find(track, 9, false, true));
        Assert.Equal(5, TrackNavigator.Find(track, 7, false, false));
        Assert.Equal(2, TrackNavigator.Find(track, 5, false, true));
        Assert.Null(TrackNavigator.Find(track, 2, false, true));
    }
}
