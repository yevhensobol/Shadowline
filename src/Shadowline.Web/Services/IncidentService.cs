using Shadowline.Core.Geometry;
using Shadowline.Core.Horizon;
using Shadowline.Core.Incidents;
using Shadowline.Core.Sites;
using Shadowline.Data;

namespace Shadowline.Web.Services;

public sealed record IncidentCase(Incident Incident, Site Site, SunTrack Track, IReadOnlyList<LanderObservation> Observations, bool HasHorizon, Shadowline.Core.Ephemeris.BodyEphemeris Ephemeris, HorizonProfile Horizon);

public sealed class IncidentService(HttpClient http, DataStore store)
{
    private readonly Dictionary<string, IncidentCase> _cases = [];
    private readonly Dictionary<string, InferenceResult> _fits = [];

    public IncidentsDocument? Document { get; private set; }

    public System.Text.Json.JsonDocument? Checks { get; private set; }

    public async Task<System.Text.Json.JsonDocument?> LoadChecksAsync(CancellationToken cancellationToken = default)
    {
        if (Checks is not null)
        {
            return Checks;
        }

        try
        {
            Checks = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync("data/incidents_checks.json", cancellationToken));
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException)
        {
            Checks = null;
        }

        return Checks;
    }

    public async Task<IncidentsDocument?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (Document is not null)
        {
            return Document;
        }

        try
        {
            await using var stream = await http.GetStreamAsync("data/incidents.json", cancellationToken);
            Document = await IncidentsReader.ReadAsync(stream, cancellationToken);
            await store.LoadAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or DataFormatException)
        {
            Document = null;
        }

        return Document;
    }

    public async Task<IncidentCase?> CaseAsync(string id, double localHorizonDeg = 0, CancellationToken cancellationToken = default)
    {
        var key = id + "|" + localHorizonDeg.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        if (_cases.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var document = await LoadAsync(cancellationToken);
        var incident = document?.Incidents?.FirstOrDefault(i => i.Id == id);
        var site = incident is null ? null : store.Catalog?.Sites.FirstOrDefault(s => s.Id == incident.Site);
        if (incident is null || site is null)
        {
            return null;
        }

        try
        {
            var ephemeris = EphemerisReader.Read(await http.GetByteArrayAsync($"data/incidents/{incident.Id}.bin", cancellationToken), incident.Grid);
            var horizon = await HorizonAsync(site.Id, cancellationToken);
            var track = SunTrack.Build(new SiteFrame(site), ephemeris, horizon ?? HorizonProfile.Flat, localHorizonDeg);
            var result = new IncidentCase(incident, site, track, (incident.Observations ?? []).Select(o => o.ToCore()).ToList(), horizon is not null, ephemeris, horizon ?? HorizonProfile.Flat);
            _cases[key] = result;
            return result;
        }
        catch (Exception exception) when (exception is HttpRequestException or DataFormatException or ArgumentException)
        {
            return null;
        }
    }

    public InferenceResult? CachedFit(string id) => _fits.GetValueOrDefault(id);

    public async Task<InferenceResult> FitAsync(IncidentCase item, InferencePrior prior, int samples, bool fit, Action<double>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        var key = item.Incident.Id + (fit ? "|fit" : "|prior");
        if (_fits.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var observations = fit ? item.Observations : item.Observations.Select(o => o with { Fit = false }).ToList();
        var random = new Random(fit ? 1 : 2);
        var accepted = new List<InferenceSample>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var s = 0; s < samples; s++)
        {
            var sample = AttitudeInference.Draw(random, prior);
            if (AttitudeInference.Fits(item.Track, observations, sample))
            {
                accepted.Add(sample);
            }

            if (watch.ElapsedMilliseconds > 40)
            {
                progress?.Invoke((double)s / samples);
                await Task.Delay(1);
                watch.Restart();
            }
        }

        await Task.Delay(1);
        var result = AttitudeInference.Summarize(item.Track, observations, accepted, samples);
        _fits[key] = result;
        return result;
    }

    private async Task<HorizonProfile?> HorizonAsync(string siteId, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"data/horizon/{Uri.EscapeDataString(siteId)}.bin", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return bytes.Length == HorizonReader.ExpectedBytes ? HorizonReader.Read(bytes) : null;
    }
}
