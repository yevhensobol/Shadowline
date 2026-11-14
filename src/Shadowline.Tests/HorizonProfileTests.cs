using Shadowline.Core.Horizon;

namespace Shadowline.Tests;

public class HorizonProfileTests
{
    [Fact]
    public void Flat_profile_is_zero_everywhere()
    {
        Assert.Equal(0.0, HorizonProfile.Flat.ElevationAt(123.4));
    }

    [Fact]
    public void Interpolates_between_samples()
    {
        var samples = new float[HorizonProfile.SampleCount];
        samples[0] = 0f;
        samples[1] = 2f;

        var profile = new HorizonProfile(samples);

        Assert.True(Math.Abs(profile.ElevationAt(0.25) - 1.0) < 1e-6);
    }

    [Fact]
    public void Wraps_around_north()
    {
        var samples = new float[HorizonProfile.SampleCount];
        samples[HorizonProfile.SampleCount - 1] = 4f;
        samples[0] = 0f;

        var profile = new HorizonProfile(samples);

        Assert.True(Math.Abs(profile.ElevationAt(359.75) - 2.0) < 1e-6);
        Assert.True(Math.Abs(profile.ElevationAt(-0.25) - 2.0) < 1e-6);
    }

    [Fact]
    public void Rejects_wrong_sample_count()
    {
        Assert.Throws<ArgumentException>(() => new HorizonProfile(new float[10]));
    }
}
