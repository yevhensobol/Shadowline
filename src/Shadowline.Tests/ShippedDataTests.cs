using Shadowline.Data;

namespace Shadowline.Tests;

public class ShippedDataTests
{
    [Fact]
    public async Task Shipped_manifest_is_valid()
    {
        await using var stream = File.OpenRead(DataPath("manifest.json"));

        var manifest = await ManifestReader.ReadAsync(stream);

        Assert.True(manifest.GridCount > 0);
    }

    [Fact]
    public async Task Shipped_catalog_is_valid()
    {
        await using var stream = File.OpenRead(DataPath("catalog.json"));

        var catalog = await SiteCatalogReader.ReadAsync(stream);

        Assert.True(catalog.Sites.Count > 0);
        foreach (var site in catalog.Sites)
        {
            Assert.False(string.IsNullOrWhiteSpace(site.Source));
            Assert.True(site.Id.StartsWith("syn-", StringComparison.Ordinal) || site.SourceUrl?.StartsWith("https://", StringComparison.Ordinal) == true);
        }
    }

    [Fact]
    public async Task Shipped_ephemeris_matches_manifest_grid()
    {
        await using var manifestStream = File.OpenRead(DataPath("manifest.json"));
        var manifest = await ManifestReader.ReadAsync(manifestStream);
        var bytes = await File.ReadAllBytesAsync(DataPath("ephemeris.bin"));

        var ephemeris = EphemerisReader.Read(bytes, manifest.Grid);

        Assert.True(ephemeris.Sun(0).Length is > 1.4e8 and < 1.6e8);
        Assert.True(ephemeris.Earth(0).Length is > 3.5e5 and < 4.1e5);
    }

    [Fact]
    public async Task Shipped_tour_points_at_real_sites_and_grid_times()
    {
        await using var manifestStream = File.OpenRead(DataPath("manifest.json"));
        var manifest = await ManifestReader.ReadAsync(manifestStream);
        await using var catalogStream = File.OpenRead(DataPath("catalog.json"));
        var catalog = await SiteCatalogReader.ReadAsync(catalogStream);
        await using var tourStream = File.OpenRead(DataPath("tour.json"));
        var tour = await TourReader.ReadAsync(tourStream);

        var windows = new[] { 24, 72, 168, 336 };
        var focuses = new[] { "map", "sky", "timeline", "metrics", "sensitivity", "compare", "view3d" };
        foreach (var step in tour.Steps!)
        {
            Assert.True(catalog.Find(step.Site) is not null || Shadowline.Core.Sites.CustomSite.TryParse(step.Site, out _));
            Assert.True(step.Time is not null);
            var index = manifest.Grid.IndexOf(step.Time!.Value);
            Assert.True(index >= 0 && index < manifest.Grid.Count);
            Assert.True(step.WindowHours is null || windows.Contains(step.WindowHours.Value));
            Assert.True(step.Focus is null || focuses.Contains(step.Focus));
        }
    }

    [Fact]
    public async Task Shipped_photos_belong_to_catalog_sites()
    {
        await using var catalogStream = File.OpenRead(DataPath("catalog.json"));
        var catalog = await SiteCatalogReader.ReadAsync(catalogStream);
        await using var stream = File.OpenRead(DataPath("photos.json"));
        var photos = await PhotosReader.ReadAsync(stream);

        foreach (var photo in photos.Photos!)
        {
            Assert.True(catalog.Find(photo.Site) is not null);
            Assert.True(photo.File.StartsWith("img/sites/", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Shipped_artemis_regions_match_the_nasa_list()
    {
        await using var stream = File.OpenRead(DataPath("artemis.json"));
        var document = await ArtemisReader.ReadAsync(stream);

        var current = document.Regions!.Where(region => region.Artemis3).Select(region => region.Name).ToHashSet();
        Assert.Equal(9, current.Count);
        foreach (var name in new[] { "Peak near Cabeus B", "Haworth", "Malapert Massif", "Mons Mouton Plateau", "Mons Mouton", "Nobile Rim 1", "Nobile Rim 2", "de Gerlache Rim 2", "Slater Plain" })
        {
            Assert.True(current.Contains(name));
        }
    }


    [Fact]
    public async Task Shipped_history_puts_the_sun_low_at_apollo_landings()
    {
        await using var catalogStream = File.OpenRead(DataPath("catalog.json"));
        var catalog = await SiteCatalogReader.ReadAsync(catalogStream);
        await using var stream = File.OpenRead(DataPath("history.json"));
        var history = await HistoryReader.ReadAsync(stream);
        var expected = new Dictionary<string, double> { ["apollo11"] = 10.65, ["apollo12"] = 5.19, ["apollo14"] = 10.36, ["apollo15"] = 12.05, ["apollo16"] = 14.76, ["apollo17"] = 13.33 };

        foreach (var item in history.Events!)
        {
            Assert.True(catalog.Find(item.Site) is not null);
            var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "data", "history", item.Id + ".bin"));
            var ephemeris = EphemerisReader.Read(bytes, item.Grid);
            if (expected.TryGetValue(item.Id, out var elevation))
            {
                var frame = new Shadowline.Core.Geometry.SiteFrame(catalog.Find(item.Site)!);
                var index = item.Grid.IndexOf(item.LandingUtc);
                var fraction = (item.LandingUtc - item.Grid.At(index)).TotalMinutes / item.Grid.Step.TotalMinutes;
                var before = frame.Observe(ephemeris.Sun(index)).ElevationDeg;
                var after = frame.Observe(ephemeris.Sun(index + 1)).ElevationDeg;
                var sun = before + (after - before) * fraction;
                Assert.True(Math.Abs(sun - elevation) < 0.1, $"{item.Id} {sun}");
            }
        }
    }

    [Fact]
    public async Task Shipped_years_follow_the_first_year()
    {
        await using var stream = File.OpenRead(DataPath("years.json"));
        var years = await YearsReader.ReadAsync(stream);

        Assert.Equal("ephemeris.bin", years.Years![0].File);
        for (var i = 1; i < years.Years.Count; i++)
        {
            Assert.Equal(years.Years[i - 1].Grid.End, years.Years[i].GridStartUtc);
            var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "data", years.Years[i].File));
            Assert.Equal(years.Years[i].GridCount * 24, bytes.Length);
        }
    }

    [Fact]
    public async Task Moon_elevation_at_dsn_stations_matches_jpl_horizons()
    {
        await using var manifestStream = File.OpenRead(DataPath("manifest.json"));
        var manifest = await ManifestReader.ReadAsync(manifestStream);
        var bytes = await File.ReadAllBytesAsync(DataPath("ephemeris.bin"));
        var ephemeris = EphemerisReader.Read(bytes, manifest.Grid);
        foreach (var item in Shadowline.Core.Comms.DsnReference.Cases)
        {
            var index = manifest.Grid.IndexOf(item.TimeUtc);
            var station = Shadowline.Core.Comms.DeepSpaceNetwork.Stations.Select((s, k) => (s, k)).First(p => p.s.Id == item.StationId).k;
            var elevation = Shadowline.Core.Comms.DeepSpaceNetwork.Elevations(ephemeris, index)[station];
            Assert.True(Math.Abs(elevation - item.ElevationDeg) < 0.005, $"{item.StationId} {elevation - item.ElevationDeg}");
        }
    }

    [Fact]
    public async Task Shipped_credits_are_valid()
    {
        await using var stream = File.OpenRead(DataPath("credits.json"));
        var credits = await CreditsReader.ReadAsync(stream);

        Assert.True(credits.Entries!.Any(entry => entry.Id == "stars"));
        Assert.True(credits.Entries!.Any(entry => entry.Id == "bluemarble"));
    }

    [Fact]
    public async Task Shipped_horizons_belong_to_catalog_sites_and_are_valid()
    {
        await using var catalogStream = File.OpenRead(DataPath("catalog.json"));
        var catalog = await SiteCatalogReader.ReadAsync(catalogStream);
        var directory = Path.Combine(AppContext.BaseDirectory, "data", "horizon");
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(directory, "*.bin"))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            Assert.True(catalog.Find(id) is not null);
            var profile = HorizonReader.Read(await File.ReadAllBytesAsync(file));
            Assert.True(profile.ElevationAt(0) is > -90 and < 90);
        }
    }

    [Fact]
    public async Task Shipped_terrain_patches_have_consistent_headers()
    {
        await using var catalogStream = File.OpenRead(DataPath("catalog.json"));
        var catalog = await SiteCatalogReader.ReadAsync(catalogStream);
        var directory = Path.Combine(AppContext.BaseDirectory, "data", "terrain");
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(directory, "*.bin"))
        {
            Assert.True(catalog.Find(Path.GetFileNameWithoutExtension(file)) is not null);
            var bytes = await File.ReadAllBytesAsync(file);
            var size = BitConverter.ToInt32(bytes, 0);
            var spacing = BitConverter.ToSingle(bytes, 4);
            Assert.True(size is > 1 and < 4096);
            Assert.True(spacing > 0);
            Assert.Equal(12 + 2 * size * size, bytes.Length);
        }
    }

    private static string DataPath(string name) => Path.Combine(AppContext.BaseDirectory, "data", name);
}
