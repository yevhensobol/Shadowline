using Shadowline.Core.Ephemeris;

namespace Shadowline.Core.Geometry;

public readonly record struct SkySnapshot(SkyPosition Sun, SkyPosition Earth, double SunRadiusDeg, double EarthRadiusDeg)
{
    public static SkySnapshot At(SiteFrame frame, BodyEphemeris ephemeris, int index)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(ephemeris);

        var sun = frame.Observe(ephemeris.Sun(index));
        var earth = frame.Observe(ephemeris.Earth(index));
        return new SkySnapshot(
            sun,
            earth,
            sun.AngularRadiusDeg(Bodies.SunRadiusKm),
            earth.AngularRadiusDeg(Bodies.EarthRadiusKm));
    }

    public bool EarthOverlapsSun => Sun.SeparationDeg(Earth) < SunRadiusDeg + EarthRadiusDeg;
}
