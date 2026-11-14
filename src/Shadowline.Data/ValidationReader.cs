using Shadowline.Core.Validation;

namespace Shadowline.Data;

public sealed record ValidationDocument(
    int FormatVersion,
    string Source,
    IReadOnlyList<ValidationFixture>? Fixtures);

public static class ValidationReader
{
    public const int SupportedFormatVersion = 1;

    public static async ValueTask<ValidationDocument> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var document = await JsonReader.ReadAsync(stream, DataJsonContext.Default.ValidationDocument, "validation", cancellationToken);
        if (document.FormatVersion != SupportedFormatVersion)
        {
            throw new DataFormatException(
                $"Unsupported validation format version {document.FormatVersion}, expected {SupportedFormatVersion}");
        }

        if (document.Fixtures is null)
        {
            throw new DataFormatException("Validation fixtures are missing");
        }

        foreach (var fixture in document.Fixtures)
        {
            if (string.IsNullOrWhiteSpace(fixture.SiteId) || string.IsNullOrWhiteSpace(fixture.Body))
            {
                throw new DataFormatException("Validation fixture has an empty site or body");
            }

            if (!double.IsFinite(fixture.AzimuthDeg) || !double.IsFinite(fixture.ElevationDeg))
            {
                throw new DataFormatException("Validation fixture has a non-finite angle");
            }
        }

        return document;
    }
}
