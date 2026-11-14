using Shadowline.Core.Ephemeris;
using Shadowline.Core.Geometry;
using Shadowline.Core.Sites;

namespace Shadowline.Core.Validation;

public static class ValidationRunner
{
    public static ValidationResult Run(
        IEnumerable<ValidationFixture> fixtures,
        Func<string, Site?> findSite,
        BodyEphemeris ephemeris)
    {
        ArgumentNullException.ThrowIfNull(fixtures);
        ArgumentNullException.ThrowIfNull(findSite);
        ArgumentNullException.ThrowIfNull(ephemeris);

        var frames = new Dictionary<string, SiteFrame>();
        var rows = new List<ValidationRow>();
        var skipped = 0;

        foreach (var fixture in fixtures)
        {
            var index = ephemeris.Grid.IndexOf(fixture.TimeUtc);
            var aligned = index >= 0 && index < ephemeris.Grid.Count && ephemeris.Grid.At(index) == fixture.TimeUtc;
            var site = findSite(fixture.SiteId);
            var body = fixture.Body.ToLowerInvariant();
            if (!aligned || site is null || body is not ("sun" or "earth"))
            {
                skipped++;
                continue;
            }

            if (!frames.TryGetValue(site.Id, out var frame))
            {
                frame = new SiteFrame(site);
                frames[site.Id] = frame;
            }

            var sky = frame.Observe(body == "sun" ? ephemeris.Sun(index) : ephemeris.Earth(index));
            rows.Add(new ValidationRow(
                fixture,
                sky.AzimuthDeg,
                sky.ElevationDeg,
                AngleDifference(sky.AzimuthDeg, fixture.AzimuthDeg),
                sky.ElevationDeg - fixture.ElevationDeg));
        }

        return new ValidationResult(rows, skipped);
    }

    public static ValidationResult Run(
        IEnumerable<ValidationFixture> fixtures,
        Func<string, Site?> findSite,
        IReadOnlyList<BodyEphemeris> ephemerides)
    {
        ArgumentNullException.ThrowIfNull(fixtures);
        ArgumentNullException.ThrowIfNull(ephemerides);

        var remaining = fixtures.ToList();
        var rows = new List<ValidationRow>();
        foreach (var ephemeris in ephemerides)
        {
            if (remaining.Count == 0)
            {
                break;
            }

            var result = Run(remaining, findSite, ephemeris);
            rows.AddRange(result.Rows);
            var done = new HashSet<ValidationFixture>(result.Rows.Select(row => row.Fixture), ReferenceEqualityComparer.Instance);
            remaining = remaining.Where(fixture => !done.Contains(fixture)).ToList();
        }

        rows.Sort((left, right) => left.Fixture.TimeUtc.CompareTo(right.Fixture.TimeUtc));
        return new ValidationResult(rows, remaining.Count);
    }

    public static double AngleDifference(double computedDeg, double referenceDeg)
    {
        var difference = (computedDeg - referenceDeg) % 360.0;
        if (difference > 180.0)
        {
            difference -= 360.0;
        }
        else if (difference <= -180.0)
        {
            difference += 360.0;
        }

        return difference;
    }
}
