using System.Globalization;
using System.Text;

namespace Shadowline.Core.Permalink;

public sealed record ScenarioPin(string SiteId, int Start, int WindowHours);

public sealed record PlannerState(string SiteId, int Cursor, int WindowHours, IReadOnlyList<ScenarioPin> Pins)
{
    public const int MaxPins = 3;
    public const int MaxWindowHours = 24 * 365;

    public string ToQuery()
    {
        var builder = new StringBuilder("?site=");
        builder.Append(Uri.EscapeDataString(SiteId));
        builder.Append("&t=").Append(Cursor.ToString(CultureInfo.InvariantCulture));
        builder.Append("&w=").Append(WindowHours.ToString(CultureInfo.InvariantCulture));

        if (Pins.Count > 0)
        {
            var pins = Pins.Take(MaxPins).Select(pin => string.Join(
                ',',
                Uri.EscapeDataString(pin.SiteId),
                pin.Start.ToString(CultureInfo.InvariantCulture),
                pin.WindowHours.ToString(CultureInfo.InvariantCulture)));
            builder.Append("&p=").Append(Uri.EscapeDataString(string.Join(';', pins)));
        }

        return builder.ToString();
    }

    public static PlannerState? Parse(string? query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        string? site = null;
        var cursor = 0;
        var windowHours = 0;
        var pins = new List<ScenarioPin>();

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = pair[..separator];
            var value = Uri.UnescapeDataString(pair[(separator + 1)..]);
            switch (key)
            {
                case "site":
                    site = value;
                    break;
                case "t":
                    cursor = ParseInt(value, 0, int.MaxValue) ?? 0;
                    break;
                case "w":
                    windowHours = ParseInt(value, 1, MaxWindowHours) ?? 0;
                    break;
                case "p":
                    pins.AddRange(ParsePins(value));
                    break;
            }
        }

        if (string.IsNullOrEmpty(site))
        {
            return null;
        }

        return new PlannerState(site, cursor, windowHours, pins.Take(MaxPins).ToArray());
    }

    private static IEnumerable<ScenarioPin> ParsePins(string value)
    {
        foreach (var entry in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split(',');
            if (parts.Length != 3)
            {
                continue;
            }

            var site = Uri.UnescapeDataString(parts[0]);
            var start = ParseInt(parts[1], 0, int.MaxValue);
            var hours = ParseInt(parts[2], 1, MaxWindowHours);
            if (site.Length > 0 && start is not null && hours is not null)
            {
                yield return new ScenarioPin(site, start.Value, hours.Value);
            }
        }
    }

    private static int? ParseInt(string text, int min, int max) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= min && number <= max
            ? number
            : null;
}
