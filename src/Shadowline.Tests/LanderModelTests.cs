using Shadowline.Core.Ephemeris;
using Shadowline.Core.Geometry;
using Shadowline.Core.Horizon;
using Shadowline.Core.Incidents;
using Shadowline.Core.Power;
using Shadowline.Core.Sites;
using Shadowline.Core.Time;

namespace Shadowline.Tests;

public class LanderModelTests
{
    private const double AuKm = 149_597_870.7;

    private static SunTrack EquatorDay()
    {
        var count = 48 * 15;
        var grid = new TimeGrid(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromMinutes(30), count);
        var values = new float[count * 6];
        for (var i = 0; i < count; i++)
        {
            var a = (95 - 190.0 * i / count) * Math.PI / 180;
            values[i * 6] = (float)(AuKm * Math.Cos(a));
            values[i * 6 + 1] = (float)(AuKm * Math.Sin(a));
            values[i * 6 + 3] = 384_400f;
        }

        var site = new Site("eq", "eq", null, 0, 0, 0);
        return SunTrack.Build(new SiteFrame(site), new BodyEphemeris(grid, values), HorizonProfile.Flat);
    }

    private static void Near(Vec3 expected, Vec3 actual)
    {
        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
        Assert.Equal(expected.Z, actual.Z, 6);
    }

    [Fact]
    public void Attitude_rotates_body_axes_into_the_local_frame()
    {
        var up = new Vec3(0, 0, 1);
        var north = new Vec3(0, 1, 0);
        Near(up, LanderAttitude.Upright.ToLocal(up));
        Near(new Vec3(1, 0, 0), new LanderAttitude(90, 90, 0).ToLocal(up));
        Near(new Vec3(-1, 0, 0), new LanderAttitude(90, 270, 0).ToLocal(up));
        Near(new Vec3(1, 0, 0), new LanderAttitude(0, 0, 90).ToLocal(north));
        Near(new Vec3(0, 0, -1), new LanderAttitude(180, 30, 0).ToLocal(up));
        var tilted = LanderGeometry.Angles(new LanderAttitude(60, 169, 0).ToLocal(LanderGeometry.Direction(349, 0)));
        Assert.Equal(349, tilted.AzimuthDeg, 6);
        Assert.Equal(60, tilted.ElevationDeg, 6);
    }

    [Fact]
    public void Crater_rim_follows_depth_and_panel_height()
    {
        Assert.Equal(Math.Atan2(3, 10) * 180 / Math.PI, LanderGeometry.CraterHorizonDeg(20, 0.2, 1), 9);
        Assert.Equal(0, LanderGeometry.CraterHorizonDeg(20, 0.1, 2.5), 9);
    }

    [Fact]
    public void Sun_moves_from_east_to_west_on_the_synthetic_day()
    {
        var track = EquatorDay();
        var first = LanderGeometry.Angles(track.Direction[30]);
        var last = LanderGeometry.Angles(track.Direction[track.Count - 30]);
        Assert.True(first.AzimuthDeg >= 80 && first.AzimuthDeg <= 100);
        Assert.True(last.AzimuthDeg >= 260 && last.AzimuthDeg <= 280);
    }

    [Fact]
    public void West_facing_panel_powers_only_in_the_afternoon_and_battery_runs_down()
    {
        var track = EquatorDay();
        var spec = new LanderSpec([new LanderPanel("top", 0, 90, 1)], 0.3, PowerModel.SystemFactorFixedEfficiency, 1, new LanderBattery(100, 1, 0.05), 50, 1, 50);
        var run = LanderModel.Run(track, spec, new LanderAttitude(90, 270, 0), track.StartUtc);
        var noon = track.Count / 2;
        Assert.Equal(0, run.PowerW[noon / 2]);
        Assert.True(run.PowerW[noon + noon / 2] > 100);
        Assert.False(run.Awake[noon - 10]);
        Assert.True(run.Awake[noon + noon / 2]);
        Assert.True(run.Events.Any(e => e.State == LanderState.Asleep));
        Assert.True(run.Events.Skip(1).Any(e => e.State == LanderState.Awake));
        Assert.True(run.PeakWatts() < spec.PeakW + 1);
    }

    [Fact]
    public void Inverse_estimate_recovers_a_west_facing_panel_from_three_events()
    {
        var track = EquatorDay();
        var truth = LanderGeometry.Direction(270, 10);
        const double threshold = 0.3;
        var on = Enumerable.Range(0, track.Count).Select(i => AttitudeInference.IsOn(track, i, truth, threshold)).ToArray();
        var firstOn = Array.IndexOf(on, true);
        var lost = Array.LastIndexOf(on, true) + 1;
        Assert.True(firstOn > track.Count / 2);
        var observations = new List<LanderObservation>
        {
            new(ObservationKind.NoPower, track.At(20), track.At(track.Count / 3), true, false, ""),
            new(ObservationKind.Power, track.At(firstOn + 20), track.At(firstOn + 24), true, false, ""),
            new(ObservationKind.PowerLost, track.At(lost - 8), track.At(lost + 8), false, true, ""),
        };

        var result = AttitudeInference.Run(track, observations, InferencePrior.Sphere, 4000);
        var prior = AttitudeInference.Run(track, observations.Select(o => o with { Fit = false }).ToList(), InferencePrior.Sphere, 4000);

        Assert.True(result.Accepted.Count > 50);
        Assert.True(result.Azimuth!.Median >= 230 && result.Azimuth!.Median <= 310);
        Assert.True(result.Scores[0].Share > prior.Scores[0].Share);
        Assert.True(result.Scores[0].Share > 0.5);
    }

    [Fact]
    public void Soft_likelihood_recovers_the_west_panel_and_tolerates_a_shifted_event()
    {
        var track = EquatorDay();
        var truth = LanderGeometry.Direction(270, 10);
        const double threshold = 0.3;
        var on = Enumerable.Range(0, track.Count).Select(i => AttitudeInference.IsOn(track, i, truth, threshold)).ToArray();
        var firstOn = Array.IndexOf(on, true);
        var lost = Array.LastIndexOf(on, true) + 1;
        var observations = new List<LanderObservation>
        {
            new(ObservationKind.NoPower, track.At(20), track.At(track.Count / 3), true, false, ""),
            new(ObservationKind.Power, track.At(firstOn + 20), track.At(firstOn + 24), true, false, ""),
            new(ObservationKind.PowerLost, track.At(lost + 30), track.At(lost + 40), true, false, ""),
        };

        var strict = AttitudeInference.Run(track, observations, InferencePrior.Sphere, 4000);
        var soft = SoftInference.Run(track, observations, InferencePrior.Sphere, 4000, new SoftOptions(12, 0.05));

        Assert.True(soft.EffectiveSize > Math.Max(strict.Accepted.Count, 50));
        Assert.True(soft.Azimuth!.Median >= 230 && soft.Azimuth!.Median <= 310);
        Assert.Equal(1, SoftInference.EventProbability(0, new SoftOptions(6, 0.05)), 6);
        Assert.Equal(0.05, SoftInference.EventProbability(1000, new SoftOptions(6, 0.05)), 6);
    }

    [Fact]
    public void Soft_distance_of_a_window_is_the_shift_that_makes_it_hold()
    {
        var track = EquatorDay();

        var shift = SoftInference.ShiftHours(track, 110, 115, i => i < 100);
        var none = SoftInference.ShiftHours(track, 10, 20, i => i < 100);

        Assert.Equal(16 * track.StepHours, shift, 9);
        Assert.Equal(0, none, 9);
    }

    [Fact]
    public void Weighted_quantiles_follow_the_weights()
    {
        var interval = SoftInference.WeightedQuantiles([(0, 1), (10, 1), (20, 98)])!;
        Assert.Equal(20, interval.Median, 6);
        var circular = SoftInference.WeightedCircular([(350, 1), (0, 2), (10, 1)])!;
        Assert.True(circular.Median < 1 || circular.Median > 359);
    }

    [Fact]
    public void Quantiles_and_circular_mean_handle_the_north_wrap()
    {
        var interval = AttitudeInference.CircularInterval([350, 355, 0, 5, 10])!;
        Assert.Equal(0, interval.Median, 6);
        Assert.Equal(350, interval.Low, 6);
        Assert.Equal(10, interval.High, 6);
    }

    [Fact]
    public void Analysis_finds_the_lunar_day_and_scores_baselines()
    {
        var track = EquatorDay();
        var days = IncidentAnalysis.LunarDays(track, track.StartUtc);
        Assert.Equal(1, days.Count);
        var truth = LanderGeometry.Direction(270, 10);
        var on = Enumerable.Range(0, track.Count).Select(i => AttitudeInference.IsOn(track, i, truth, 0.3)).ToArray();
        var firstOn = Array.IndexOf(on, true);
        var noPower = new LanderObservation(ObservationKind.NoPower, track.At(20), track.At(track.Count / 3), true, false, "");
        var sunUp = IncidentAnalysis.Score(track, new PredictiveModel("sunUp", IncidentAnalysis.SunUp(track), null), noPower);
        var reported = IncidentAnalysis.Score(track, new PredictiveModel("fixed", IncidentAnalysis.FixedPanel(track, 270, 10, 0.3), null), noPower);
        Assert.Equal(0, sunUp.Probability);
        Assert.Equal(1, sunUp.Brier);
        Assert.Equal(1, reported.Probability);
        Assert.Equal(0, reported.Brier);

        var samples = new List<InferenceSample> { new(270, 10, 0.3), new(90, 10, 0.3) };
        var share = IncidentAnalysis.OnShare(track, samples, 0, 1);
        Assert.Equal(0.5, share[firstOn + 10], 9);
        var prediction = IncidentAnalysis.PredictDays(track, [new InferenceSample(270, 10, 0.3)], days, track.StartUtc).Single();
        Assert.Equal(1, prediction.PowerShare);
        Assert.Equal(firstOn * track.StepHours, prediction.FirstPowerUtcHours!.Median, 6);
    }

    [Fact]
    public void Tip_over_risk_favours_panels_on_every_face()
    {
        var track = EquatorDay();
        var layouts = TipOverRisk.Layouts(2);
        var options = new TipOptions(LoadW: 100);
        var noon = track.Count / 2;
        var result = TipOverRisk.Analyze(track, noon, noon, 1, layouts, options, 300);
        var top = result.Curves.Single(c => c.LayoutId == "top").Survive[0];
        var cube = result.Curves.Single(c => c.LayoutId == "cube").Survive[0];
        Assert.True(cube > 0.95);
        Assert.True(top > 0.3 && top < 0.7);
        Assert.True(result.Curves.Single(c => c.LayoutId == "top").UprightSurvives[0]);
        Assert.Equal(700 * 0.8 * 0.95 / 100, options.BatteryHours, 9);
        var night = TipOverRisk.Analyze(track, 2, 2, 1, layouts, options with { BatteryWh = 10 }, 50);
        Assert.True(night.Curves.All(c => c.Survive[0] < 0.5));
        var draws = new Random(3);
        var side = Enumerable.Range(0, 200).Select(_ => TipOverRisk.DrawAttitude(draws, TipPrior.OnSide).TiltDeg).ToList();
        Assert.True(side.All(t => t >= 60 && t <= 100));
    }
}
