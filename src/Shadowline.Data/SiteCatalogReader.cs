namespace Shadowline.Data;

public static class SiteCatalogReader
{
    public static async ValueTask<SiteCatalog> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var document = await JsonReader.ReadAsync(stream, DataJsonContext.Default.SiteCatalogDocument, "catalog", cancellationToken);
        if (document.Sites is null)
        {
            throw new DataFormatException("Catalog has no sites");
        }

        return new SiteCatalog(document.Sites, document.Synthetic);
    }
}
