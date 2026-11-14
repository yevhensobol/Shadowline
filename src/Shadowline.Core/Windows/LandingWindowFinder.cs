using Shadowline.Core.Tracks;

namespace Shadowline.Core.Windows;

public sealed record WindowCriteria(int MissionSteps, double MinSunShare, double MinLinkShare, double MinLandingSunDeg, bool LinkAtLanding = true);

public sealed record LandingWindow(int First, int Last, int Best, double SunShare, double LinkShare, int SunStepsAfterLanding, double LandingSunDeg, int Breaks = 0);

public static class LandingWindowFinder
{
    public static IReadOnlyList<LandingWindow> Find(BitTrack sun, BitTrack link, Func<int, double> sunElevationDeg, WindowCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(sun);
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(sunElevationDeg);
        ArgumentNullException.ThrowIfNull(criteria);
        if (sun.Length != link.Length)
        {
            throw new ArgumentException("Tracks differ in length");
        }

        var n = sun.Length;
        var w = Math.Max(1, criteria.MissionSteps);
        var sunSum = Prefix(sun);
        var linkSum = Prefix(link);
        var sunRun = RunAhead(sun);
        var windows = new List<LandingWindow>();
        var first = -1;
        var best = -1;
        double bestSun = -1, bestLink = 0, bestEl = 0;
        for (var i = 0; i + w <= n; i++)
        {
            var ok = sun[i] && (!criteria.LinkAtLanding || link[i]);
            double sunShare = 0, linkShare = 0, elevation = 0;
            if (ok)
            {
                sunShare = (sunSum[i + w] - sunSum[i]) / (double)w;
                linkShare = (linkSum[i + w] - linkSum[i]) / (double)w;
                ok = sunShare >= criteria.MinSunShare && linkShare >= criteria.MinLinkShare;
            }

            if (ok)
            {
                elevation = sunElevationDeg(i);
                ok = elevation >= criteria.MinLandingSunDeg;
            }

            if (ok)
            {
                if (first < 0)
                {
                    first = i;
                    bestSun = -1;
                }

                if (sunShare > bestSun)
                {
                    best = i;
                    bestSun = sunShare;
                    bestLink = linkShare;
                    bestEl = elevation;
                }
            }
            else if (first >= 0)
            {
                windows.Add(new LandingWindow(first, i - 1, best, bestSun, bestLink, sunRun[best], bestEl));
                first = -1;
            }
        }

        if (first >= 0)
        {
            windows.Add(new LandingWindow(first, n - w, best, bestSun, bestLink, sunRun[best], bestEl));
        }

        return windows;
    }

    public static IReadOnlyList<LandingWindow> Merge(IReadOnlyList<LandingWindow> windows, int maxGapSteps)
    {
        ArgumentNullException.ThrowIfNull(windows);
        var merged = new List<LandingWindow>();
        foreach (var window in windows)
        {
            if (merged.Count > 0 && window.First - merged[^1].Last - 1 <= maxGapSteps)
            {
                var previous = merged[^1];
                var better = window.SunShare > previous.SunShare ? window : previous;
                merged[^1] = better with { First = previous.First, Last = window.Last, Breaks = previous.Breaks + window.Breaks + 1 };
            }
            else
            {
                merged.Add(window);
            }
        }

        return merged;
    }

    private static int[] Prefix(BitTrack track)
    {
        var sum = new int[track.Length + 1];
        for (var i = 0; i < track.Length; i++)
        {
            sum[i + 1] = sum[i] + (track[i] ? 1 : 0);
        }

        return sum;
    }

    private static int[] RunAhead(BitTrack track)
    {
        var run = new int[track.Length + 1];
        for (var i = track.Length - 1; i >= 0; i--)
        {
            run[i] = track[i] ? run[i + 1] + 1 : 0;
        }

        return run;
    }
}
