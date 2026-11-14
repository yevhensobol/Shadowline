namespace Shadowline.Data;

public sealed record HistoryEvent(
    string Id,
    string Site,
    DateTimeOffset LandingUtc,
    DateTimeOffset GridStartUtc,
    int GridStepMinutes,
    int GridCount,
    string? Source,
    IReadOnlyDictionary<string, string>? Title)
{
    public Shadowline.Core.Time.TimeGrid Grid => new(GridStartUtc, TimeSpan.FromMinutes(GridStepMinutes), GridCount);
}

public sealed record HistoryDocument(int FormatVersion, string? Frame, IReadOnlyList<HistoryEvent>? Events);

public static class HistoryReader
{
    public const int SupportedFormatVersion = 1;

    private static readonly string[] RequiredLanguages = ["en", "uk"];

    public static async ValueTask<HistoryDocument> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var document = await JsonReader.ReadAsync(stream, DataJsonContext.Default.HistoryDocument, "history", cancellationToken);
        if (document.FormatVersion != SupportedFormatVersion)
        {
            throw new DataFormatException(
                $"Unsupported history format version {document.FormatVersion}, expected {SupportedFormatVersion}");
        }

        if (document.Events is null || document.Events.Count == 0)
        {
            throw new DataFormatException("History has no events");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in document.Events)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id) || item.Id.Contains('/') || item.Id.Contains('.'))
            {
                throw new DataFormatException("History event has an empty, duplicate or unsafe id");
            }

            if (item.GridStepMinutes <= 0 || item.GridCount <= 0)
            {
                throw new DataFormatException($"History event '{item.Id}' has an empty grid");
            }

            var end = item.GridStartUtc + TimeSpan.FromMinutes((double)item.GridStepMinutes * item.GridCount);
            if (item.LandingUtc < item.GridStartUtc || item.LandingUtc >= end)
            {
                throw new DataFormatException($"History event '{item.Id}' lands outside its grid");
            }

            foreach (var language in RequiredLanguages)
            {
                if (item.Title is null || !item.Title.TryGetValue(language, out var title) || string.IsNullOrWhiteSpace(title))
                {
                    throw new DataFormatException($"History event '{item.Id}' has no title for '{language}'");
                }
            }
        }

        return document;
    }
}
