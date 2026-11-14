using Shadowline.Core.Sites;

namespace Shadowline.Data;

internal sealed record SiteCatalogDocument(IReadOnlyList<Site> Sites, bool Synthetic = false);

public sealed class SiteCatalog
{
    private readonly Dictionary<string, Site> byId = new(StringComparer.Ordinal);

    public SiteCatalog(IEnumerable<Site> sites, bool synthetic = false)
    {
        Sites = sites.ToArray();
        IsSynthetic = synthetic;
        foreach (var site in Sites)
        {
            Validate(site);
            if (!byId.TryAdd(site.Id, site))
            {
                throw new DataFormatException($"Duplicate site id '{site.Id}'");
            }
        }
    }

    public IReadOnlyList<Site> Sites { get; }

    public bool IsSynthetic { get; }

    public Site? Find(string id) => byId.GetValueOrDefault(id);

    private static void Validate(Site? site)
    {
        if (site is null || string.IsNullOrWhiteSpace(site.Id))
        {
            throw new DataFormatException("Site without id");
        }

        if (site.LatitudeDeg is < -90 or > 90)
        {
            throw new DataFormatException($"Site '{site.Id}' has latitude outside -90..90");
        }

        if (site.LongitudeEastDeg is < -180 or >= 360)
        {
            throw new DataFormatException($"Site '{site.Id}' has longitude outside -180..360");
        }

        if (site.RockAbundance is < 0 or > 1)
        {
            throw new DataFormatException($"Site '{site.Id}' has rock abundance outside 0..1");
        }
    }
}
