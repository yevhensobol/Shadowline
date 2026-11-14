namespace Shadowline.Core.Time;

public sealed record TimeGrid
{
    public TimeGrid(DateTimeOffset start, TimeSpan step, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Start = start;
        Step = step;
        Count = count;
    }

    public DateTimeOffset Start { get; }

    public TimeSpan Step { get; }

    public int Count { get; }

    public DateTimeOffset End => At(Count);

    public DateTimeOffset At(int index) => Start + Step * index;

    public int IndexOf(DateTimeOffset moment) => (int)Math.Floor((moment - Start) / Step);
}
