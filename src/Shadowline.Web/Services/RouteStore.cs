using Shadowline.Core.Routes;
using Shadowline.Web.Components;

namespace Shadowline.Web.Services;

public sealed class RouteStore
{
    public MoonGlobe.RouteData? Data { get; private set; }

    public RouteSummary? Summary { get; private set; }

    public string? SiteId { get; private set; }

    public void Set(MoonGlobe.RouteData? data, RouteSummary? summary, string? siteId)
    {
        Data = data is { Count: > 1 } ? data : null;
        Summary = Data is null ? null : summary;
        SiteId = Data is null ? null : siteId;
    }

    public bool BelongsTo(string siteId) => Data is not null && string.Equals(SiteId, siteId, StringComparison.Ordinal);
}
