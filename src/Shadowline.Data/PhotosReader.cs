namespace Shadowline.Data;

public sealed record SitePhoto(string Site, string File, string Source, string Credit, IReadOnlyDictionary<string, string>? Caption);

public sealed record PhotosDocument(int FormatVersion, IReadOnlyList<SitePhoto>? Photos);

public static class PhotosReader
{
    public const int SupportedFormatVersion = 1;

    private static readonly string[] RequiredLanguages = ["en", "uk"];

    public static async ValueTask<PhotosDocument> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var document = await JsonReader.ReadAsync(stream, DataJsonContext.Default.PhotosDocument, "photos", cancellationToken);
        if (document.FormatVersion != SupportedFormatVersion)
        {
            throw new DataFormatException(
                $"Unsupported photos format version {document.FormatVersion}, expected {SupportedFormatVersion}");
        }

        if (document.Photos is null)
        {
            throw new DataFormatException("Photos list is missing");
        }

        foreach (var photo in document.Photos)
        {
            if (string.IsNullOrWhiteSpace(photo.Site) || string.IsNullOrWhiteSpace(photo.Credit))
            {
                throw new DataFormatException("Photo needs a site and a credit");
            }

            if (string.IsNullOrWhiteSpace(photo.File) || photo.File.Contains("..", StringComparison.Ordinal) || photo.File.StartsWith('/') || photo.File.Contains("://", StringComparison.Ordinal))
            {
                throw new DataFormatException($"Photo for '{photo.Site}' needs a relative file path");
            }

            if (!Uri.TryCreate(photo.Source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new DataFormatException($"Photo for '{photo.Site}' needs an https source");
            }

            foreach (var language in RequiredLanguages)
            {
                if (photo.Caption is null || !photo.Caption.TryGetValue(language, out var text) || string.IsNullOrWhiteSpace(text))
                {
                    throw new DataFormatException($"Photo for '{photo.Site}' has no caption for '{language}'");
                }
            }
        }

        return document;
    }

    public static SitePhoto? Find(PhotosDocument? document, string siteId) =>
        document?.Photos?.FirstOrDefault(photo => photo.Site == siteId);
}
