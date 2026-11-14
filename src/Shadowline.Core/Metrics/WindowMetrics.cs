namespace Shadowline.Core.Metrics;

public sealed record WindowMetrics(
    double SunFraction,
    double EarthFraction,
    TimeSpan LongestDarkness,
    TimeSpan LongestEarthGap,
    TimeSpan EarthOverSun);
