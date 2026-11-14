using System.Globalization;
using Shadowline.Core.Replan;

namespace Shadowline.Web.Services;

public static class ViolationText
{
    public static string Label(Localizer localizer, RequirementKind kind) => localizer[kind switch
    {
        RequirementKind.SunFraction => "violation.sun",
        RequirementKind.Darkness => "violation.darkness",
        RequirementKind.EarthFraction => "violation.earth",
        RequirementKind.EarthGap => "violation.gap",
        _ => "violation.outside"
    }];

    public static string Describe(Localizer localizer, Violation violation) => violation.Kind switch
    {
        RequirementKind.SunFraction or RequirementKind.EarthFraction => localizer.Format(
            "violation.fraction",
            Label(localizer, violation.Kind),
            Percent(violation.Actual),
            Percent(violation.Required)),
        RequirementKind.Darkness or RequirementKind.EarthGap => localizer.Format(
            "violation.duration",
            Label(localizer, violation.Kind),
            localizer.Hours(violation.Actual),
            localizer.Hours(violation.Required)),
        _ => Label(localizer, violation.Kind)
    };

    public static string Describe(Localizer localizer, RequirementAdjustment adjustment) => adjustment.Kind switch
    {
        RequirementKind.SunFraction or RequirementKind.EarthFraction => localizer.Format(
            "adjustment.line",
            Label(localizer, adjustment.Kind),
            Percent(adjustment.From),
            Percent(adjustment.To)),
        _ => localizer.Format(
            "adjustment.line",
            Label(localizer, adjustment.Kind),
            localizer.Hours(adjustment.From),
            localizer.Hours(adjustment.To))
    };

    private static string Percent(double fraction) => (fraction * 100).ToString("F0", CultureInfo.InvariantCulture) + "%";
}
