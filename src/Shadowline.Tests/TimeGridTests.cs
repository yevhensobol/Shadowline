using Shadowline.Core.Time;

namespace Shadowline.Tests;

public class TimeGridTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void At_adds_whole_steps()
    {
        var grid = new TimeGrid(Start, TimeSpan.FromMinutes(30), 100);

        Assert.Equal(Start.AddHours(1), grid.At(2));
    }

    [Fact]
    public void IndexOf_rounds_down()
    {
        var grid = new TimeGrid(Start, TimeSpan.FromMinutes(30), 100);

        Assert.Equal(1, grid.IndexOf(Start.AddMinutes(45)));
    }

    [Fact]
    public void IndexOf_before_start_is_negative()
    {
        var grid = new TimeGrid(Start, TimeSpan.FromMinutes(30), 100);

        Assert.Equal(-1, grid.IndexOf(Start.AddMinutes(-1)));
    }

    [Fact]
    public void End_is_one_step_past_the_last_index()
    {
        var grid = new TimeGrid(Start, TimeSpan.FromMinutes(30), 48);

        Assert.Equal(Start.AddDays(1), grid.End);
    }

    [Fact]
    public void Non_positive_step_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeGrid(Start, TimeSpan.Zero, 10));
    }

    [Fact]
    public void Negative_count_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeGrid(Start, TimeSpan.FromMinutes(30), -1));
    }
}
