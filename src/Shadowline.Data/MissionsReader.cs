namespace Shadowline.Data;

public sealed record MissionInfo(
    string Site,
    string? Agency,
    IReadOnlyDictionary<string, string>? Date,
    IReadOnlyDictionary<string, string>? Summary,
    IReadOnlyList<ExternalLink>? Links);

public sealed record MissionsDocument(int FormatVersion, IReadOnlyList<MissionInfo>? Missions);

public static class MissionsReader
{
    public const int SupportedFormatVersion = 1;

    private static readonly string[] RequiredLanguages = ["en", "uk"];

    public static async ValueTask<MissionsDocument> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var document = await JsonReader.ReadAsync(stream, DataJsonContext.Default.MissionsDocument, "missions", cancellationToken);
        if (document.FormatVersion != SupportedFormatVersion)
        {
            throw new DataFormatException(
                $"Unsupported missions format version {document.FormatVersion}, expected {SupportedFormatVersion}");
        }

        if (document.Missions is null || document.Missions.Count == 0)
        {
            throw new DataFormatException("Missions list is missing");
        }

        var sites = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mission in document.Missions)
        {
            if (string.IsNullOrWhiteSpace(mission.Site) || !sites.Add(mission.Site))
            {
                throw new DataFormatException("Mission has an empty or duplicate site");
            }

            foreach (var language in RequiredLanguages)
            {
                if (mission.Summary is null || !mission.Summary.TryGetValue(language, out var text) || string.IsNullOrWhiteSpace(text))
                {
                    throw new DataFormatException($"Mission '{mission.Site}' has no summary in '{language}'");
                }
            }

            if (mission.Links is not { Count: > 0 })
            {
                throw new DataFormatException($"Mission '{mission.Site}' has no links");
            }

            ExternalLink.Validate(mission.Site, mission.Links);
        }

        return document;
    }

    public static MissionInfo? Find(MissionsDocument? document, string siteId) =>
        document?.Missions?.FirstOrDefault(mission => mission.Site == siteId);
}
