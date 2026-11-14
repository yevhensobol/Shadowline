namespace Shadowline.Core.Replan;

public sealed record MissionRequirements(
    TimeSpan Duration,
    double MinSunFraction,
    TimeSpan MaxDarkness,
    double MinEarthFraction,
    TimeSpan MaxEarthGap);
