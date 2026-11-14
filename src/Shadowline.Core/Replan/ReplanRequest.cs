using Shadowline.Core.Sites;
using Shadowline.Core.Time;
using Shadowline.Core.Tracks;

namespace Shadowline.Core.Replan;

public sealed record SiteTracks(Site Site, SkyTracks Tracks);

public sealed record ReplanRequest(
    TimeGrid Grid,
    IReadOnlyList<SiteTracks> Sites,
    string PreferredSiteId,
    DateTimeOffset PlannedStart,
    MissionRequirements Requirements,
    TimeSpan MaxDelay,
    TimeSpan StartStep);
