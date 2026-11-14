using System.Text.Json;
using System.Text.Json.Serialization;
using Shadowline.Core.Reports;

namespace Shadowline.Web.Services;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SiteReport))]
internal sealed partial class ReportJsonContext : JsonSerializerContext
{
}

public static class ReportJson
{
    public static string Write(SiteReport report) => JsonSerializer.Serialize(report, ReportJsonContext.Default.SiteReport);
}
