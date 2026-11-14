using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shadowline.Data;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ArtemisDocument))]
[JsonSerializable(typeof(PhotosDocument))]
[JsonSerializable(typeof(CreditsDocument))]
[JsonSerializable(typeof(SiteCatalogDocument))]
[JsonSerializable(typeof(YearsDocument))]
[JsonSerializable(typeof(DataManifest))]
[JsonSerializable(typeof(HistoryDocument))]
internal sealed partial class DataJsonContext : JsonSerializerContext;
