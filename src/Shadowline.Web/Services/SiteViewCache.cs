using System.IO.Compression;
using System.Text.Json;
using Shadowline.Core.Horizon;
using Shadowline.Core.Radio;
using Shadowline.Core.Sites;
using Shadowline.Core.Terrain;
using Shadowline.Data;

namespace Shadowline.Web.Services;

public sealed class SiteViewCache(DataStore store, HttpClient http)
{
    private readonly Dictionary<string, SiteView> _views = [];
    private readonly Dictionary<string, HorizonProfile> _horizons = [];
    private readonly Dictionary<string, CustomTerrain> _custom = [];
    private readonly Dictionary<DemTileKey, short[]?> _tiles = [];
    private bool _horizonsLoaded;
    private bool _polarLoaded;
    private PolarDem? _polar;

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_horizonsLoaded)
        {
            return;
        }

        foreach (var site in store.Catalog!.Sites)
        {
            var profile = await TryLoadHorizonAsync(site.Id, cancellationToken);
            if (profile is not null)
            {
                _horizons[site.Id] = profile;
            }
        }

        _views.Clear();
        _horizonsLoaded = true;
    }

    public sealed record CustomTerrain(byte[] Tile, float[] Horizon, double ElevationM);

    public CustomTerrain? TerrainFor(string siteId) => _custom.GetValueOrDefault(siteId);

    private readonly Dictionary<string, TerrainPatch?> _patches = [];

    public async Task<TerrainPatch?> PatchAsync(string siteId, CancellationToken cancellationToken = default)
    {
        if (_patches.TryGetValue(siteId, out var cached))
        {
            return cached;
        }

        TerrainPatch? patch = null;
        try
        {
            if (TerrainFor(siteId) is { } custom)
            {
                patch = TerrainPatch.Parse(custom.Tile);
            }
            else if (!siteId.StartsWith(CustomSite.Prefix, StringComparison.Ordinal))
            {
                using var response = await http.GetAsync($"data/terrain/{Uri.EscapeDataString(siteId)}.bin", cancellationToken);
                patch = response.IsSuccessStatusCode ? TerrainPatch.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken)) : null;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or ArgumentException)
        {
            patch = null;
        }

        _patches[siteId] = patch;
        return patch;
    }

    private readonly Dictionary<string, Task<bool>> _preparing = [];

    public Task<bool> PrepareAsync(string siteId, CancellationToken cancellationToken = default)
    {
        if (_custom.ContainsKey(siteId) || !CustomSite.TryParse(siteId, out _))
        {
            return Task.FromResult(false);
        }

        if (_preparing.TryGetValue(siteId, out var running))
        {
            return running;
        }

        var task = PrepareCoreAsync(siteId, cancellationToken);
        _preparing[siteId] = task;
        return task;
    }

    private async Task<bool> PrepareCoreAsync(string siteId, CancellationToken cancellationToken)
    {
        await Task.Yield();
        try
        {
            return await PrepareSiteAsync(siteId, cancellationToken);
        }
        finally
        {
            _preparing.Remove(siteId);
        }
    }

    private async Task<bool> PrepareSiteAsync(string siteId, CancellationToken cancellationToken)
    {
        if (_custom.ContainsKey(siteId) || !CustomSite.TryParse(siteId, out var site))
        {
            return false;
        }

        var keys = GlobalDem.TilesAround(site.LatitudeDeg, site.LongitudeEastDeg, 300000);
        foreach (var key in keys.Where(key => !_tiles.ContainsKey(key)))
        {
            _tiles[key] = await TryLoadTileAsync(key, cancellationToken);
        }

        if (keys.Any(key => _tiles[key] is null))
        {
            return false;
        }

        await Task.Yield();
        var dem = new GlobalDem(key => _tiles.GetValueOrDefault(key));
        Func<double, double, double> sample = dem.Sample;
        HorizonProfile horizon;
        byte[] tile;
        var fine = await PoleSamplerAsync(site.LatitudeDeg, site.LongitudeEastDeg, PolarHorizon.NearRangeM, dem.Sample, cancellationToken);
        if (fine is not null)
        {
            sample = fine.Value.Sample;
            await Task.Yield();
            horizon = PolarHorizon.Compute(site.LatitudeDeg, site.LongitudeEastDeg, fine.Value.Window, fine.Value.Coarse, dem.Sample);
            await Task.Yield();
            tile = LocalTerrain.Tile(sample, site.LatitudeDeg, site.LongitudeEastDeg, PolarPatchSize, PolarPatchSpacingM);
            _polarSites.Add(siteId);
            _poleSites.Add(siteId);
        }
        else
        {
            if (site.LatitudeDeg < -80)
            {
                var polar = await PolarAsync(cancellationToken);
                if (polar?.Sample(site.LatitudeDeg, site.LongitudeEastDeg) is not null)
                {
                    sample = polar.Over(dem.Sample);
                    _polarSites.Add(siteId);
                }
            }

            horizon = LocalTerrain.Horizon(sample, site.LatitudeDeg, site.LongitudeEastDeg);
            await Task.Yield();
            tile = LocalTerrain.Tile(sample, site.LatitudeDeg, site.LongitudeEastDeg);
        }

        var elevations = new float[HorizonProfile.SampleCount];
        for (var i = 0; i < elevations.Length; i++)
        {
            elevations[i] = (float)horizon.ElevationAt(i * HorizonProfile.StepDeg);
        }

        _custom[siteId] = new CustomTerrain(tile, elevations, sample(site.LatitudeDeg, site.LongitudeEastDeg));
        _horizons[siteId] = horizon;
        foreach (var key in _views.Keys.Where(key => key == siteId || key.EndsWith("|" + siteId, StringComparison.Ordinal)).ToList())
        {
            _views.Remove(key);
        }

        return true;
    }

    public sealed record TerrainSampler(Func<double, double, double> Sample, bool Polar);

    public async Task<TerrainSampler?> SamplerAroundAsync(double latDeg, double lonDeg, double rangeM, CancellationToken cancellationToken = default)
    {
        var keys = GlobalDem.TilesAround(latDeg, lonDeg, rangeM);
        foreach (var key in keys.Where(key => !_tiles.ContainsKey(key)))
        {
            _tiles[key] = await TryLoadTileAsync(key, cancellationToken);
        }

        if (keys.Any(key => _tiles[key] is null))
        {
            return null;
        }

        var dem = new GlobalDem(key => _tiles.GetValueOrDefault(key));
        if (await PoleSamplerAsync(latDeg, lonDeg, SensitivityRadiusM, dem.Sample, cancellationToken) is { } fine)
        {
            return new TerrainSampler(fine.Sample, true);
        }

        if (latDeg < -80 && await PolarAsync(cancellationToken) is { } polar && polar.Sample(latDeg, lonDeg) is not null)
        {
            return new TerrainSampler(polar.Over(dem.Sample), true);
        }

        return new TerrainSampler(dem.Sample, false);
    }

    public sealed record RadioMap(MastViewshed Viewshed, byte[] Pixels);

    private readonly Dictionary<(double, double), Task<RadioMap?>> _radio = [];

    public Task<RadioMap?> RadioAsync(double latDeg, double lonDeg)
    {
        var key = (Math.Round(latDeg, 5), Math.Round(lonDeg, 5));
        if (!_radio.TryGetValue(key, out var task))
        {
            task = RadioCoreAsync(latDeg, lonDeg);
            _radio[key] = task;
        }

        return task;
    }

    private async Task<RadioMap?> RadioCoreAsync(double latDeg, double lonDeg)
    {
        await Task.Yield();
        if (await PoleSamplerAsync(latDeg, lonDeg, MastViewshed.RangeM + 1000, (_, _) => 0) is not { } fine)
        {
            return null;
        }

        await Task.Yield();
        var (x, y) = PolarDem.Project(latDeg, lonDeg);
        double? Sample(double px, double py) => fine.Window.Sample(px, py) ?? fine.Coarse.SampleXY(px, py);
        if (MastViewshed.Compute(x, y, Sample) is not { } viewshed)
        {
            return null;
        }

        await Task.Yield();
        return new RadioMap(viewshed, viewshed.ShadedMap(Sample));
    }

    private readonly HashSet<string> _polarSites = [];

    public bool UsesPolarGrid(string siteId) => _polarSites.Contains(siteId);

    private readonly HashSet<string> _poleSites = [];

    public bool UsesPoleTiles(string siteId) => _poleSites.Contains(siteId);

    public const int PolarPatchSize = 401;
    public const double SensitivityRadiusM = 50000;
    public const double PolarPatchSpacingM = 100;

    private readonly Dictionary<PoleTileKey, Task<short[]?>> _poleTiles = [];
    private Task<bool>? _poleTask;
    private PoleGrid? _poleGrid;
    private PolarDem? _poleCoarse;

    public readonly record struct PoleSampler(PoleWindow Window, PolarDem Coarse, Func<double, double, double> Sample);

    public async Task<PoleSampler?> PoleSamplerAsync(double latDeg, double lonDeg, double radiusM, Func<double, double, double> fallback, CancellationToken cancellationToken = default)
    {
        if (latDeg > -79 || !await LoadPoleAsync(cancellationToken) || _poleGrid is not { } grid || _poleCoarse is not { } coarse)
        {
            return null;
        }

        var (x, y) = PolarDem.Project(latDeg, lonDeg);
        if (!grid.Contains(x, y))
        {
            return null;
        }

        var loaded = new Dictionary<PoleTileKey, short[]?>();
        foreach (var key in grid.TilesAround(x, y, radiusM))
        {
            if (!_poleTiles.TryGetValue(key, out var pending))
            {
                pending = TryLoadPoleTileAsync(grid, key);
                _poleTiles[key] = pending;
            }

            loaded[key] = await pending;
        }

        var window = grid.Window(x, y, radiusM, key => loaded.GetValueOrDefault(key));
        if (window.Sample(x, y) is null)
        {
            return null;
        }

        double Sample(double lat, double lon)
        {
            var (px, py) = PolarDem.Project(lat, lon);
            return window.Sample(px, py) ?? coarse.SampleXY(px, py) ?? fallback(lat, lon);
        }

        return new PoleSampler(window, coarse, Sample);
    }

    private Task<bool> LoadPoleAsync(CancellationToken cancellationToken) => _poleTask ??= LoadPoleCoreAsync();

    private async Task<bool> LoadPoleCoreAsync()
    {
        var cancellationToken = CancellationToken.None;
        try
        {
            using var document = JsonDocument.Parse(await http.GetStringAsync("data/pole/pole.json", cancellationToken));
            var root = document.RootElement;
            var present = root.GetProperty("present").EnumerateArray().Select(item => new PoleTileKey(item[0].GetInt32(), item[1].GetInt32()));
            _poleGrid = new PoleGrid(root.GetProperty("sizePx").GetInt32(), root.GetProperty("spacingM").GetDouble(), root.GetProperty("halfExtentM").GetDouble(), root.GetProperty("tilePx").GetInt32(), root.GetProperty("unitM").GetDouble(), present);
            _poleCoarse = PolarDem.ParseDelta(Gunzip(await http.GetByteArrayAsync("data/pole/coarse.bin.gz", cancellationToken)));
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or InvalidDataException)
        {
            _poleGrid = null;
            _poleCoarse = null;
        }

        return _poleGrid is not null && _poleCoarse is not null;
    }

    private async Task<short[]?> TryLoadPoleTileAsync(PoleGrid grid, PoleTileKey key)
    {
        try
        {
            var bytes = Gunzip(await http.GetByteArrayAsync($"data/pole/80/{key.FileName}"));
            return PoleGrid.Decode(bytes, grid.TilePx, grid.TilePx);
        }
        catch (Exception exception) when (exception is HttpRequestException or ArgumentException or InvalidDataException)
        {
            return null;
        }
    }

    private static byte[] Gunzip(byte[] bytes)
    {
        if (bytes.Length < 2 || bytes[0] != 0x1f || bytes[1] != 0x8b)
        {
            return bytes;
        }

        using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private async Task<PolarDem?> PolarAsync(CancellationToken cancellationToken)
    {
        if (_polarLoaded)
        {
            return _polar;
        }

        try
        {
            _polar = PolarDem.Parse(await http.GetByteArrayAsync("img/scene/pole_height.bin", cancellationToken));
        }
        catch (HttpRequestException)
        {
            _polar = null;
        }

        _polarLoaded = true;
        return _polar;
    }

    private async Task<short[]?> TryLoadTileAsync(DemTileKey key, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await http.GetByteArrayAsync($"data/dem/{key.FileName}", cancellationToken);
            if (bytes.Length != GlobalDem.TileSize * GlobalDem.TileSize * 2)
            {
                return null;
            }

            var values = new short[GlobalDem.TileSize * GlobalDem.TileSize];
            Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
            return values;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public Site? Resolve(string siteId) =>
        store.Catalog!.Find(siteId) ?? (CustomSite.TryParse(siteId, out var custom) ? custom : null);

    private int _version;

    public SiteView Get(string siteId)
    {
        if (_version != store.Version)
        {
            _version = store.Version;
            _views.Clear();
        }

        if (_views.TryGetValue(siteId, out var cached))
        {
            return cached;
        }

        var site = Resolve(siteId) ?? throw new KeyNotFoundException(siteId);
        var hasTerrain = _horizons.TryGetValue(siteId, out var horizon);
        var view = new SiteView(site, store.Ephemeris!, hasTerrain ? horizon! : HorizonProfile.Flat, hasTerrain);
        _views[siteId] = view;
        return view;
    }

    public SiteView Get(string siteId, string? historyId, Shadowline.Core.Ephemeris.BodyEphemeris? ephemeris)
    {
        if (historyId is null || ephemeris is null)
        {
            return Get(siteId);
        }

        var key = historyId + "|" + siteId;
        if (_views.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var site = Resolve(siteId) ?? throw new KeyNotFoundException(siteId);
        var hasTerrain = _horizons.TryGetValue(siteId, out var horizon);
        var view = new SiteView(site, ephemeris, hasTerrain ? horizon! : HorizonProfile.Flat, hasTerrain);
        _views[key] = view;
        return view;
    }

    private async Task<HorizonProfile?> TryLoadHorizonAsync(string siteId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync($"data/horizon/{Uri.EscapeDataString(siteId)}.bin", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return bytes.Length == HorizonReader.ExpectedBytes ? HorizonReader.Read(bytes) : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or DataFormatException)
        {
            return null;
        }
    }
}
