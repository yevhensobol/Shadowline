namespace Shadowline.Core.Validation;

public sealed record ValidationFixture(
    string SiteId,
    string Body,
    DateTimeOffset TimeUtc,
    double AzimuthDeg,
    double ElevationDeg);
