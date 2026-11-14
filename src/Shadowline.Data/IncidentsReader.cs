using Shadowline.Core.Incidents;

namespace Shadowline.Data;

public sealed record IncidentVehicle(
    double? DryMassKg,
    double? LaunchMassKg,
    double? HeightM,
    double? DiameterM,
    IReadOnlyList<double>? SizeM,
    string? Panels,
    string? Battery,
    IReadOnlyList<string>? Sources);

public sealed record IncidentAttitude(
    string? Summary,
    double? TiltDeg,
    double? PanelAzimuthDeg,
    double? PanelTiltDeg,
    double? CraterDiameterM,
    IReadOnlyList<string>? Sources);

public sealed record IncidentObservation(string Type, DateTimeOffset FromUtc, DateTimeOffset ToUtc, string Use, string? Note, string? NoteUk, IReadOnlyList<string>? Sources, bool Derived = false)
{
    public LanderObservation ToCore() => new(
        Enum.Parse<ObservationKind>(Type, ignoreCase: true),
        FromUtc,
        ToUtc,
        Use == "fit",
        Use == "check",
        Note ?? "");
}

public sealed record Incident(
    string Id,
    string Site,
    string Kind,
    string? Agency,
    DateTimeOffset LandingUtc,
    DateTimeOffset GridStartUtc,
    int GridStepMinutes,
    int GridCount,
    IncidentVehicle? Vehicle,
    IncidentAttitude? Attitude,
    IReadOnlyList<IncidentObservation>? Observations,
    string? Notes = null,
    string? LandingNote = null)
{
    public Shadowline.Core.Time.TimeGrid Grid => new(GridStartUtc, TimeSpan.FromMinutes(GridStepMinutes), GridCount);
}

public sealed record IncidentSource(string Title, string Url);

public sealed record IncidentsDocument(int FormatVersion, string? Frame, IReadOnlyList<Incident>? Incidents, IReadOnlyDictionary<string, IncidentSource>? Sources);

public static class IncidentsReader
{
    public const int SupportedFormatVersion = 1;

    private static readonly HashSet<string> Uses = ["fit", "check", "none"];

    public static async ValueTask<IncidentsDocument> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var document = await JsonReader.ReadAsync(stream, DataJsonContext.Default.IncidentsDocument, "incidents", cancellationToken);
        if (document.FormatVersion != SupportedFormatVersion)
        {
            throw new DataFormatException($"Unsupported incidents format version {document.FormatVersion}, expected {SupportedFormatVersion}");
        }

        if (document.Incidents is null || document.Incidents.Count == 0)
        {
            throw new DataFormatException("Incidents file has no incidents");
        }

        var sources = document.Sources ?? new Dictionary<string, IncidentSource>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in document.Incidents)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id) || item.Id.Contains('/') || item.Id.Contains('.'))
            {
                throw new DataFormatException("Incident has an empty, duplicate or unsafe id");
            }

            if (item.GridStepMinutes <= 0 || item.GridCount <= 0)
            {
                throw new DataFormatException($"Incident '{item.Id}' has an empty grid");
            }

            var end = item.GridStartUtc + TimeSpan.FromMinutes((double)item.GridStepMinutes * item.GridCount);
            if (item.LandingUtc < item.GridStartUtc || item.LandingUtc >= end)
            {
                throw new DataFormatException($"Incident '{item.Id}' lands outside its grid");
            }

            foreach (var observation in item.Observations ?? [])
            {
                if (!Enum.TryParse<ObservationKind>(observation.Type, true, out _) || !Uses.Contains(observation.Use) || observation.ToUtc < observation.FromUtc)
                {
                    throw new DataFormatException($"Incident '{item.Id}' has an invalid observation");
                }

                foreach (var source in observation.Sources ?? [])
                {
                    if (!sources.ContainsKey(source))
                    {
                        throw new DataFormatException($"Incident '{item.Id}' cites unknown source '{source}'");
                    }
                }
            }
        }

        return document;
    }
}
