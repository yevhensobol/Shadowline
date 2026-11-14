using System.Text.Json.Serialization;
using Shadowline.Core.Time;

namespace Shadowline.Data;

public sealed record DataManifest(
    int FormatVersion,
    string Kind,
    DateTimeOffset BuiltAtUtc,
    DateTimeOffset GridStartUtc,
    int GridStepMinutes,
    int GridCount,
    IReadOnlyList<DataSource> Sources)
{
    [JsonIgnore]
    public TimeGrid Grid => new(GridStartUtc, TimeSpan.FromMinutes(GridStepMinutes), GridCount);
}

public sealed record DataSource(string Name, string Url, string Citation);
