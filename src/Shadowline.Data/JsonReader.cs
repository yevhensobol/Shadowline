using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Shadowline.Data;

internal static class JsonReader
{
    public static async ValueTask<T> ReadAsync<T>(
        Stream stream,
        JsonTypeInfo<T> typeInfo,
        string name,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken)
                ?? throw new DataFormatException($"{name} is empty");
        }
        catch (JsonException exception)
        {
            throw new DataFormatException($"{name} is not valid: {exception.Message}", exception);
        }
    }
}
