using Shadowline.Core.Time;
using Shadowline.Core.Tracks;

namespace Shadowline.Core.Metrics;

public static class WindowEvaluator
{
    public static WindowMetrics Evaluate(SkyTracks tracks, TimeGrid grid, int startIndex, int lengthSteps)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(grid);
        if (lengthSteps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lengthSteps));
        }

        var sun = tracks.SunVisible.Slice(startIndex, lengthSteps);
        var earth = tracks.EarthVisible.Slice(startIndex, lengthSteps);
        var overSun = tracks.EarthOverSun.Slice(startIndex, lengthSteps);

        return new WindowMetrics(
            sun.Fraction(),
            earth.Fraction(),
            grid.Step * sun.LongestRun(false),
            grid.Step * earth.LongestRun(false),
            grid.Step * overSun.CountSet());
    }
}
