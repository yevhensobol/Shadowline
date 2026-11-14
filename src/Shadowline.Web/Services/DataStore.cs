using Shadowline.Core.Ephemeris;
using Shadowline.Data;
using System.Formats.Tar;

namespace Shadowline.Web.Services;

public sealed class DataStore(HttpClient http)
{
    public DataManifest? Manifest { get; private set; }

    public SiteCatalog? Catalog { get; private set; }

    public BodyEphemeris? Ephemeris { get; private set; }

    public TourDocument? Tour { get; private set; }

    public CreditsDocument? Credits { get; private set; }

    public PhotosDocument? Photos { get; private set; }

    public ArtemisDocument? Artemis { get; private set; }


    public HistoryDocument? History { get; private set; }

    public YearsDocument? Years { get; private set; }

    public string? Year { get; private set; }

    public int Version { get; private set; }

    private DataManifest? _baseManifest;

    private BodyEphemeris? _baseEphemeris;

    private readonly Dictionary<string, BodyEphemeris> _years = [];

    public bool IsBaseYear => Years?.Years is not { Count: > 0 } list || Year == list[0].Id;

    public string YearQuery => IsBaseYear || Year is null ? string.Empty : "&y=" + Uri.EscapeDataString(Year);

    public static string YearLabel(string id) =>
        int.TryParse(id, out var year) ? $"{year}–{(year + 1) % 100:D2}" : id;

    public async Task<bool> SetYearAsync(string? id, CancellationToken cancellationToken = default)
    {
        if (Years?.Years is not { Count: > 0 } list || _baseManifest is null || _baseEphemeris is null)
        {
            return false;
        }

        var entry = list.FirstOrDefault(item => item.Id == id) ?? list[0];
        if (entry.Id == Year)
        {
            return true;
        }

        if (await LoadYearAsync(entry, cancellationToken) is not { } ephemeris)
        {
            return false;
        }

        Manifest = _baseManifest with { GridStartUtc = entry.GridStartUtc, GridStepMinutes = entry.GridStepMinutes, GridCount = entry.GridCount };
        Ephemeris = ephemeris;
        Year = entry.Id;
        Version++;
        return true;
    }

    public async Task<BodyEphemeris?> LoadYearAsync(YearEntry entry, CancellationToken cancellationToken = default)
    {
        if (Years?.Years is { Count: > 0 } list && entry.Id == list[0].Id)
        {
            return _baseEphemeris;
        }

        if (_years.TryGetValue(entry.Id, out var cached))
        {
            return cached;
        }

        try
        {
            var bytes = await http.GetByteArrayAsync($"data/{entry.File}", cancellationToken);
            var ephemeris = EphemerisReader.Read(bytes, entry.Grid);
            _years[entry.Id] = ephemeris;
            return ephemeris;
        }
        catch (Exception exception) when (exception is HttpRequestException or DataFormatException)
        {
            return null;
        }
    }

    public async Task ApplyYearAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            if (pair.StartsWith("y=", StringComparison.Ordinal))
            {
                await SetYearAsync(Uri.UnescapeDataString(pair[2..]), cancellationToken);
                return;
            }
        }
    }

    private readonly Dictionary<string, BodyEphemeris> _history = [];

    public HistoryEvent? HistoryFor(string siteId) =>
        History?.Events?.FirstOrDefault(item => item.Site == siteId);

    public HistoryEvent? FindHistory(string id) =>
        History?.Events?.FirstOrDefault(item => item.Id == id);

    public async Task<BodyEphemeris?> LoadHistoryAsync(HistoryEvent item, CancellationToken cancellationToken = default)
    {
        if (_history.TryGetValue(item.Id, out var cached))
        {
            return cached;
        }

        try
        {
            var bytes = await http.GetByteArrayAsync($"data/history/{item.Id}.bin", cancellationToken);
            var ephemeris = EphemerisReader.Read(bytes, item.Grid);
            _history[item.Id] = ephemeris;
            return ephemeris;
        }
        catch (Exception exception) when (exception is HttpRequestException or DataFormatException)
        {
            return null;
        }
    }

    public async Task<CreditsDocument?> LoadCreditsAsync(CancellationToken cancellationToken = default)
    {
        if (Credits is not null)
        {
            return Credits;
        }

        try
        {
            await using var stream = await http.GetStreamAsync("data/credits.json", cancellationToken);
            Credits = await CreditsReader.ReadAsync(stream, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or DataFormatException)
        {
            Credits = null;
        }

        return Credits;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (Manifest is not null && Catalog is not null && Ephemeris is not null)
        {
            return;
        }

        await using var manifestStream = await http.GetStreamAsync("data/manifest.json", cancellationToken);
        var manifest = await ManifestReader.ReadAsync(manifestStream, cancellationToken);

        await using var catalogStream = await http.GetStreamAsync("data/catalog.json", cancellationToken);
        var catalog = await SiteCatalogReader.ReadAsync(catalogStream, cancellationToken);

        var ephemerisBytes = await http.GetByteArrayAsync("data/ephemeris.bin", cancellationToken);
        var ephemeris = EphemerisReader.Read(ephemerisBytes, manifest.Grid);

        Tour = await TryLoadTourAsync(cancellationToken);
        Photos = await TryLoadPhotosAsync(cancellationToken);
        Artemis = await TryLoadArtemisAsync(cancellationToken);
        History = await TryLoadHistoryAsync(cancellationToken);
        Years = await TryLoadYearsAsync(cancellationToken);
        Manifest = manifest;
        Catalog = catalog;
        Ephemeris = ephemeris;
        _baseManifest = manifest;
        _baseEphemeris = ephemeris;
        Year = Years?.Years?[0].Id;
    }

    private async Task<YearsDocument?> TryLoadYearsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await http.GetStreamAsync("data/years.json", cancellationToken);
            var document = await YearsReader.ReadAsync(stream, cancellationToken);
            return document.Years![0].File == "ephemeris.bin" ? document : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or DataFormatException)
        {
            return null;
        }
    }

    private async Task<HistoryDocument?> TryLoadHistoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await http.GetStreamAsync("data/history.json", cancellationToken);
            return await HistoryReader.ReadAsync(stream, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or DataFormatException)
        {
            return null;
        }
    }


    private async Task<ArtemisDocument?> TryLoadArtemisAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await http.GetStreamAsync("data/artemis.json", cancellationToken);
            return await ArtemisReader.ReadAsync(stream, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or DataFormatException)
        {
            return null;
        }
    }

    private async Task<PhotosDocument?> TryLoadPhotosAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await http.GetStreamAsync("data/photos.json", cancellationToken);
            return await PhotosReader.ReadAsync(stream, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or DataFormatException)
        {
            return null;
        }
    }

    private async Task<TourDocument?> TryLoadTourAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await http.GetStreamAsync("data/tour.json", cancellationToken);
            return await TourReader.ReadAsync(stream, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or DataFormatException)
        {
            return null;
        }
    }
}
