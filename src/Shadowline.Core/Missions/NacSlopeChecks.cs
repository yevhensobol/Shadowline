namespace Shadowline.Core.Missions;

public sealed record NacSlopeCheck(
    string Key,
    string Product,
    string ProductUrl,
    double DtmScaleM,
    double AreaM,
    double ModelSpacingM,
    double ModelSlopeDeg,
    double ModelFootprintSlopeDeg,
    double FineBaselineM,
    double NacMedianDeg,
    double NacP10Deg,
    double NacP90Deg,
    double WideBaselineM,
    double NacMedianWideDeg);

public static class NacSlopeChecks
{
    public static IReadOnlyList<NacSlopeCheck> All { get; } =
    [
        new("im1", "NAC_DTM_MALAPERTA01", "https://data.lroc.im-ldi.com/lroc/view_rdr/NAC_DTM_MALAPERTA01", 3, 300, 1000, 3.58, 4.85, 6, 11.7, 9.3, 15.3, 84, 12.1),
        new("im2", "NAC_DTM_NOBILE03", "https://data.lroc.im-ldi.com/lroc/view_rdr/NAC_DTM_NOBILE03", 4, 50, 100, 2.02, 2.45, 8, 3.2, 1.4, 4.9, 80, 3.0),
    ];
}
