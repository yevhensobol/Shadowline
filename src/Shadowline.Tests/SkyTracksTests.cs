using Shadowline.Core.Ephemeris;
using Shadowline.Core.Horizon;
using Shadowline.Core.Sites;
using Shadowline.Core.Time;
using Shadowline.Core.Tracks;

namespace Shadowline.Tests;

public class SkyTracksTests
{
    private const double SunKm = 149_597_870.7;
    private const double EarthKm = 384_400.0;

    private static readonly Site Equator = new("eq", "eq", null, 0, 0, 0);

    private static TimeGrid Grid(int count) =>
        new(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromMinutes(30), count);

    private static BodyEphemeris Rows(params (double sx, double sy, double sz, double ex, double ey, double ez)[] rows)
    {
        var values = new float[rows.Length * 6];
        for (var i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            values[i * 6] = (float)r.sx;
            values[i * 6 + 1] = (float)r.sy;
            values[i * 6 + 2] = (float)r.sz;
            values[i * 6 + 3] = (float)r.ex;
            values[i * 6 + 4] = (float)r.ey;
            values[i * 6 + 5] = (float)r.ez;
        }

        return new BodyEphemeris(Grid(rows.Length), values);
    }

    [Fact]
    public void Earth_over_the_sun_counts_only_while_the_sun_is_up()
    {
        var ephemeris = Rows((SunKm, 0, 0, EarthKm, 0, 0), (-SunKm, 0, 0, -EarthKm, 0, 0));

        var tracks = SkyTracks.Build(Equator, ephemeris, HorizonProfile.Flat);

        Assert.True(tracks.EarthOverSun[0]);
        Assert.False(tracks.EarthOverSun[1]);
    }

    [Fact]
    public void Earth_counts_as_in_view_when_its_centre_clears_the_horizon()
    {
        var below = -0.5 * Math.PI / 180;
        var ephemeris = Rows((SunKm, 0, 0, Math.Sin(below) * EarthKm, Math.Cos(below) * EarthKm, 0));

        var tracks = SkyTracks.Build(Equator, ephemeris, HorizonProfile.Flat);

        Assert.False(tracks.EarthVisible[0]);
    }

    [Fact]
    public void Sun_overhead_is_visible_and_sun_below_is_not()
    {
        var ephemeris = Rows((SunKm, 0, 0, EarthKm, 0, 0), (-SunKm, 0, 0, -EarthKm, 0, 0));

        var tracks = SkyTracks.Build(Equator, ephemeris, HorizonProfile.Flat);

        Assert.True(tracks.SunVisible[0]);
        Assert.False(tracks.SunVisible[1]);
        Assert.True(tracks.EarthVisible[0]);
        Assert.False(tracks.EarthVisible[1]);
    }

    [Fact]
    public void Terrain_mask_hides_a_sun_just_above_the_flat_horizon()
    {
        var elevations = new float[HorizonProfile.SampleCount];
        Array.Fill(elevations, 5f);
        var mask = new HorizonProfile(elevations);
        var angle = 2.0 * Math.PI / 180.0;
        var ephemeris = Rows((SunKm * Math.Sin(angle), 0, SunKm * Math.Cos(angle), EarthKm, 0, 0));

        var flat = SkyTracks.Build(Equator, ephemeris, HorizonProfile.Flat);
        var masked = SkyTracks.Build(Equator, ephemeris, mask);

        Assert.True(flat.SunVisible[0]);
        Assert.False(masked.SunVisible[0]);
    }

    [Fact]
    public void Earth_over_sun_requires_overlapping_discs()
    {
        var offset = 3.0 * Math.PI / 180.0;
        var ephemeris = Rows(
            (SunKm, 0, 0, EarthKm, 0, 0),
            (SunKm, 0, 0, EarthKm * Math.Cos(offset), EarthKm * Math.Sin(offset), 0));

        var tracks = SkyTracks.Build(Equator, ephemeris, HorizonProfile.Flat);

        Assert.True(tracks.EarthOverSun[0]);
        Assert.False(tracks.EarthOverSun[1]);
    }

    [Fact]
    public void Tracks_have_the_length_of_the_grid()
    {
        var ephemeris = Rows((SunKm, 0, 0, EarthKm, 0, 0), (SunKm, 0, 0, EarthKm, 0, 0), (SunKm, 0, 0, EarthKm, 0, 0));

        var tracks = SkyTracks.Build(Equator, ephemeris, HorizonProfile.Flat);

        Assert.Equal(3, tracks.SunVisible.Length);
        Assert.Equal(3, tracks.EarthVisible.Length);
        Assert.Equal(3, tracks.EarthOverSun.Length);
    }
}
