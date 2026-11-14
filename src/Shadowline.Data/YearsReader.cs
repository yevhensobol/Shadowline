namespace Shadowline.Data;

public sealed record YearEntry(string Id, DateTimeOffset GridStartUtc, int GridStepMinutes, int GridCount, string File)
{
    public Shadowline.Core.Time.TimeGrid Grid => new(GridStartUtc, TimeSpan.FromMinutes(GridStepMinutes), GridCount);
}

public sealed record YearsDocument(int FormatVersion, string? Frame, IReadOnlyList<YearEntry>? Years);

public static class YearsReader
{
    public const int SupportedFormatVersion = 1;

    public static async ValueTask<YearsDocument> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var document = await JsonReader.ReadAsync(stream, DataJsonContext.Default.YearsDocument, "years", cancellationToken);
        if (document.FormatVersion != SupportedFormatVersion)
        {
            throw new DataFormatException(
                $"Unsupported years format version {document.FormatVersion}, expected {SupportedFormatVersion}");
        }

        if (document.Years is null || document.Years.Count == 0)
        {
            throw new DataFormatException("Years list is empty");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var year in document.Years)
        {
            if (string.IsNullOrWhiteSpace(year.Id) || !ids.Add(year.Id))
            {
                throw new DataFormatException("Year has an empty or duplicate id");
            }

            if (year.GridStepMinutes <= 0 || year.GridCount <= 0)
            {
                throw new DataFormatException($"Year '{year.Id}' has an empty grid");
            }

            if (year.File != "ephemeris.bin" && !(year.File.StartsWith("years/", StringComparison.Ordinal) && year.File.EndsWith(".bin", StringComparison.Ordinal) && !year.File.Contains("..", StringComparison.Ordinal)))
            {
                throw new DataFormatException($"Year '{year.Id}' points to an unexpected file");
            }
        }

        return document;
    }
}
