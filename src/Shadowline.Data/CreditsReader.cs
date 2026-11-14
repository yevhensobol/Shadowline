namespace Shadowline.Data;

public sealed record CreditEntry(
    string Id,
    string Title,
    string Author,
    string Source,
    string License,
    IReadOnlyDictionary<string, string>? UsedIn,
    string? Kind = null);

public sealed record CreditsDocument(int FormatVersion, IReadOnlyList<CreditEntry>? Entries);

public static class CreditsReader
{
    public const int SupportedFormatVersion = 1;

    private static readonly string[] RequiredLanguages = ["en", "uk"];

    public static readonly string[] Kinds = ["data", "software", "research"];

    public static async ValueTask<CreditsDocument> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var document = await JsonReader.ReadAsync(stream, DataJsonContext.Default.CreditsDocument, "credits", cancellationToken);
        if (document.FormatVersion != SupportedFormatVersion)
        {
            throw new DataFormatException(
                $"Unsupported credits format version {document.FormatVersion}, expected {SupportedFormatVersion}");
        }

        if (document.Entries is null || document.Entries.Count == 0)
        {
            throw new DataFormatException("Credits have no entries");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in document.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id) || !ids.Add(entry.Id))
            {
                throw new DataFormatException("Credit entry has an empty or duplicate id");
            }

            if (string.IsNullOrWhiteSpace(entry.Title) || string.IsNullOrWhiteSpace(entry.Author) || string.IsNullOrWhiteSpace(entry.License))
            {
                throw new DataFormatException($"Credit entry '{entry.Id}' misses a title, author or license");
            }

            if (!Uri.TryCreate(entry.Source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new DataFormatException($"Credit entry '{entry.Id}' needs an https source");
            }

            if (entry.Kind is { } kind && !Kinds.Contains(kind))
            {
                throw new DataFormatException($"Credit entry '{entry.Id}' has an unknown kind '{kind}'");
            }

            foreach (var language in RequiredLanguages)
            {
                if (entry.UsedIn is null || !entry.UsedIn.TryGetValue(language, out var text) || string.IsNullOrWhiteSpace(text))
                {
                    throw new DataFormatException($"Credit entry '{entry.Id}' has no usedIn text for '{language}'");
                }
            }
        }

        return document;
    }
}
