using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shadowline.Data;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(DataManifest))]
[JsonSerializable(typeof(SiteCatalogDocument))]
[JsonSerializable(typeof(ValidationDocument))]
[JsonSerializable(typeof(TourDocument))]
[JsonSerializable(typeof(CreditsDocument))]
[JsonSerializable(typeof(PhotosDocument))]
[JsonSerializable(typeof(ArtemisDocument))]
[JsonSerializable(typeof(HistoryDocument))]
[JsonSerializable(typeof(YearsDocument))]
[JsonSerializable(typeof(IncidentsDocument))]
[JsonSerializable(typeof(MissionsDocument))]
internal sealed partial class DataJsonContext : JsonSerializerContext;
