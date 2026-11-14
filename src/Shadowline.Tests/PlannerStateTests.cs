using Shadowline.Core.Permalink;

namespace Shadowline.Tests;

public class PlannerStateTests
{
    [Fact]
    public void Query_round_trips()
    {
        var state = new PlannerState(
            "syn-ridge",
            300,
            168,
            [new ScenarioPin("syn-pole", 10, 72), new ScenarioPin("syn-rim", 20, 24)]);

        var parsed = PlannerState.Parse(state.ToQuery());

        Assert.Equal("syn-ridge", parsed!.SiteId);
        Assert.Equal(300, parsed.Cursor);
        Assert.Equal(168, parsed.WindowHours);
        Assert.Equal(state.Pins.ToArray(), parsed.Pins.ToArray());
    }

    [Fact]
    public void Site_ids_with_reserved_characters_survive()
    {
        var state = new PlannerState("a b&c=d,e;f", 1, 24, [new ScenarioPin("x,y;z&1", 2, 24)]);

        var parsed = PlannerState.Parse(state.ToQuery());

        Assert.Equal("a b&c=d,e;f", parsed!.SiteId);
        Assert.Equal("x,y;z&1", Assert.Single(parsed.Pins).SiteId);
    }

    [Fact]
    public void Missing_site_gives_no_state()
    {
        Assert.Null(PlannerState.Parse("?t=3&w=24"));
        Assert.Null(PlannerState.Parse(""));
        Assert.Null(PlannerState.Parse(null));
    }

    [Fact]
    public void Invalid_numbers_fall_back_to_defaults()
    {
        var parsed = PlannerState.Parse("?site=a&t=-5&w=abc");

        Assert.Equal(0, parsed!.Cursor);
        Assert.Equal(0, parsed.WindowHours);
    }

    [Fact]
    public void Out_of_range_window_is_ignored()
    {
        var parsed = PlannerState.Parse("?site=a&w=999999");

        Assert.Equal(0, parsed!.WindowHours);
    }

    [Fact]
    public void Malformed_pins_are_dropped_and_valid_ones_kept()
    {
        var parsed = PlannerState.Parse("?site=a&p=" + Uri.EscapeDataString("bad;s1,5,24;s2,x,24;s3,1;s4,2,48"));

        Assert.Equal(new[] { new ScenarioPin("s1", 5, 24), new ScenarioPin("s4", 2, 48) }, parsed!.Pins.ToArray());
    }

    [Fact]
    public void Pins_are_limited_to_the_maximum()
    {
        var pins = Enumerable.Range(0, 5).Select(i => new ScenarioPin("s", i, 24)).ToArray();
        var state = new PlannerState("a", 0, 24, pins);

        Assert.Equal(PlannerState.MaxPins, state.ToQuery().Split("%3B").Length);
        Assert.Equal(PlannerState.MaxPins, PlannerState.Parse(state.ToQuery())!.Pins.Count);
    }

    [Fact]
    public void Unknown_keys_and_noise_are_ignored()
    {
        var parsed = PlannerState.Parse("?x=1&&=2&site=a&junk&t=7");

        Assert.Equal("a", parsed!.SiteId);
        Assert.Equal(7, parsed.Cursor);
    }
}
