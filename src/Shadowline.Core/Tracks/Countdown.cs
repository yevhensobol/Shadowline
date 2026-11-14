namespace Shadowline.Core.Tracks;

public readonly record struct Countdown(bool Active, int? StepsToChange)
{
    public static Countdown At(BitTrack track, int index)
    {
        ArgumentNullException.ThrowIfNull(track);
        var current = track[index];
        for (var i = index + 1; i < track.Length; i++)
        {
            if (track[i] != current)
            {
                return new Countdown(current, i - index);
            }
        }

        return new Countdown(current, null);
    }
}
