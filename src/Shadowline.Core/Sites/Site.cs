namespace Shadowline.Core.Sites;

public sealed record Site(
    string Id,
    string Name,
    string? Mission,
    double LatitudeDeg,
    double LongitudeEastDeg,
    double ElevationM,
    string? Source = null,
    string? SourceUrl = null,
    string? NameUk = null,
    double? RockAbundance = null,
    string? MissionUk = null);
