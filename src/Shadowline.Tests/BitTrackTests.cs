using Shadowline.Core.Tracks;

namespace Shadowline.Tests;

public class BitTrackTests
{
    [Fact]
    public void Fraction_is_set_bits_over_length()
    {
        Assert.Equal(0.5, Track("1100").Fraction());
    }

    [Fact]
    public void Empty_track_has_zero_fraction()
    {
        Assert.Equal(0.0, new BitTrack(0).Fraction());
    }

    [Fact]
    public void Longest_run_of_ones()
    {
        Assert.Equal(3, Track("0011101").LongestRun(true));
    }

    [Fact]
    public void Longest_run_of_zeros()
    {
        Assert.Equal(2, Track("0011101").LongestRun(false));
    }

    [Fact]
    public void Runs_report_start_and_length()
    {
        var runs = Track("0110111").Runs(true).ToArray();

        Assert.Equal(new[] { new Run(1, 2), new Run(4, 3) }, runs);
    }

    [Fact]
    public void Run_touching_the_end_is_reported()
    {
        var runs = Track("0011").Runs(true).ToArray();

        Assert.Equal(new[] { new Run(2, 2) }, runs);
    }

    [Fact]
    public void And_keeps_common_bits()
    {
        var result = Track("1100").And(Track("1010"));

        Assert.Equal(1, result.CountSet());
        Assert.True(result[0]);
    }

    [Fact]
    public void Or_merges_bits()
    {
        var result = Track("1100").Or(Track("1010"));

        Assert.Equal(3, result.CountSet());
        Assert.False(result[3]);
    }

    [Fact]
    public void Not_does_not_set_bits_past_the_end()
    {
        Assert.Equal(70, new BitTrack(70).Not().CountSet());
    }

    [Fact]
    public void Combining_tracks_of_different_length_is_rejected()
    {
        var track = Track("11");

        Assert.Throws<ArgumentException>(() => track.And(Track("111")));
    }

    [Fact]
    public void Slice_copies_the_requested_range()
    {
        var slice = Track("0110101").Slice(1, 4);

        Assert.Equal(4, slice.Length);
        Assert.Equal(3, slice.CountSet());
        Assert.True(slice[0]);
        Assert.True(slice[3]);
    }

    [Fact]
    public void Slice_outside_the_track_is_rejected()
    {
        var track = Track("0110");

        Assert.Throws<ArgumentOutOfRangeException>(() => track.Slice(2, 3));
    }

    [Fact]
    public void Bytes_round_trip()
    {
        var original = new BitTrack(100);
        for (var i = 0; i < original.Length; i += 3)
        {
            original[i] = true;
        }

        var copy = BitTrack.FromBytes(original.ToBytes(), original.Length);

        Assert.Equal(original.CountSet(), copy.CountSet());
        Assert.Equal(original.ToBytes(), copy.ToBytes());
    }

    [Fact]
    public void Bytes_of_wrong_size_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => BitTrack.FromBytes(new byte[3], 100));
    }

    [Fact]
    public void Index_outside_the_track_is_rejected()
    {
        var track = new BitTrack(3);

        Assert.Throws<ArgumentOutOfRangeException>(() => track[3]);
    }

    private static BitTrack Track(string pattern)
    {
        var track = new BitTrack(pattern.Length);
        for (var i = 0; i < pattern.Length; i++)
        {
            track[i] = pattern[i] == '1';
        }

        return track;
    }
}
