namespace Shadowline.Data;

public static class ManifestReader
{
    public const int SupportedFormatVersion = 1;

    public static async ValueTask<DataManifest> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var manifest = await JsonReader.ReadAsync(stream, DataJsonContext.Default.DataManifest, "manifest", cancellationToken);
        Validate(manifest);
        return manifest;
    }

    private static void Validate(DataManifest manifest)
    {
        if (manifest.FormatVersion != SupportedFormatVersion)
        {
            throw new DataFormatException(
                $"Unsupported data format version {manifest.FormatVersion}, expected {SupportedFormatVersion}");
        }

        if (manifest.Kind is not ("synthetic" or "real"))
        {
            throw new DataFormatException($"Unknown data kind '{manifest.Kind}'");
        }

        if (manifest.GridStepMinutes <= 0 || manifest.GridCount <= 0)
        {
            throw new DataFormatException("Time grid must have positive step and count");
        }

        if (manifest.Sources is null)
        {
            throw new DataFormatException("Sources are missing");
        }
    }
}
