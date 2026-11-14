using Shadowline.Core.Tracks;

namespace Shadowline.Tests;

public class CountdownTests
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
    public void Active_reports_steps_until_it_turns_off()
    {
        var result = Countdown.At(Track("11100"), 1);

        Assert.True(result.Active);
        Assert.Equal(2, result.StepsToChange);
    }

    [Fact]
    public void Inactive_reports_steps_until_it_turns_on()
    {
        var result = Countdown.At(Track("0011"), 0);

        Assert.False(result.Active);
        Assert.Equal(2, result.StepsToChange);
    }

    [Fact]
    public void No_change_in_range_gives_null()
    {
        var result = Countdown.At(Track("1111"), 2);

        Assert.True(result.Active);
        Assert.Null(result.StepsToChange);
    }
}
