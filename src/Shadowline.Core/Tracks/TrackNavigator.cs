namespace Shadowline.Core.Tracks;

public static class TrackNavigator
{
    public static int? Find(BitTrack track, int index, bool forward, bool rising)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (forward)
        {
            for (var i = Math.Max(index + 1, 1); i < track.Length; i++)
            {
                if (track[i] != track[i - 1] && track[i] == rising)
                {
                    return i;
                }
            }
        }
        else
        {
            for (var i = Math.Min(index - 1, track.Length - 1); i >= 1; i--)
            {
                if (track[i] != track[i - 1] && track[i] == rising)
                {
                    return i;
                }
            }
        }

        return null;
    }
}
