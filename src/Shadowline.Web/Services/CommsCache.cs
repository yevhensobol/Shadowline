using System.Runtime.CompilerServices;
using Shadowline.Core.Comms;
using Shadowline.Core.Ephemeris;
using Shadowline.Core.Tracks;

namespace Shadowline.Web.Services;

public sealed class CommsCache
{
    private readonly ConditionalWeakTable<BodyEphemeris, BitTrack> _tracks = new();

    public BitTrack Dsn(BodyEphemeris ephemeris) => _tracks.GetValue(ephemeris, DeepSpaceNetwork.Track);
}
