namespace Shadowline.Data;

public sealed record ArtemisSite(string Name, double Lat, double Lon);

public sealed record ArtemisRegion(string Code, string Name, bool Artemis3, double Lat, double Lon, IReadOnlyList<ArtemisSite>? Sites);

public sealed record ArtemisDocument(int FormatVersion, string? Source, string? Announcement, IReadOnlyList<ArtemisRegion>? Regions);

public static class ArtemisReader
{
    public const int SupportedFormatVersion = 1;

    public static async ValueTask<ArtemisDocument> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var document = await JsonReader.ReadAsync(stream, DataJsonContext.Default.ArtemisDocument, "artemis", cancellationToken);
        if (document.FormatVersion != SupportedFormatVersion)
        {
            throw new DataFormatException(
                $"Unsupported artemis format version {document.FormatVersion}, expected {SupportedFormatVersion}");
        }

        if (document.Regions is null || document.Regions.Count == 0)
        {
            throw new DataFormatException("Artemis file has no regions");
        }

        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var region in document.Regions)
        {
            if (string.IsNullOrWhiteSpace(region.Code) || !codes.Add(region.Code) || string.IsNullOrWhiteSpace(region.Name))
            {
                throw new DataFormatException("Artemis region has an empty or duplicate code or no name");
            }

            if (!Polar(region.Lat, region.Lon) || region.Sites is not { Count: > 0 } sites || sites.Any(site => string.IsNullOrWhiteSpace(site.Name) || !Polar(site.Lat, site.Lon)))
            {
                throw new DataFormatException($"Artemis region '{region.Code}' has invalid coordinates or no sites");
            }
        }

        return document;
    }

    private static bool Polar(double lat, double lon) =>
        double.IsFinite(lat) && double.IsFinite(lon) && lat is >= -90 and <= -75 && lon is >= -180 and <= 180;
}
