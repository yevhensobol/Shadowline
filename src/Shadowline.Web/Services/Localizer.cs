using Microsoft.JSInterop;
using Microsoft.VisualBasic;
using Shadowline.Core.Finder;
using Shadowline.Core.Localization;
using Shadowline.Core.Reports;
using Shadowline.Core.Sites;
using System.Globalization;
using Strings = Shadowline.Core.Localization.Strings;
namespace Shadowline.Web.Services;


public sealed class Localizer(IJSRuntime js)
{
    public string Language { get; private set; } = Strings.English;

    public event Action? Changed;

    public string this[string key] => Strings.Get(Language, key);

    public string Name(Site site) =>
        Language == Strings.Ukrainian && site.NameUk is { Length: > 0 } uk ? uk : site.Name;

    public string? Mission(Site site) =>
        Language == Strings.Ukrainian && site.MissionUk is { Length: > 0 } uk ? uk : site.Mission;

    public string Note(Reference reference) =>
        (Language == Strings.Ukrainian ? reference.NoteUk : null) ?? reference.Note ?? string.Empty;

    public string Lat(double degrees, int digits) =>
        Number(Math.Abs(degrees), digits) + "° " + this[degrees < 0 ? "common.dir.s" : "common.dir.n"];

    public string Lon(double degrees, int digits) =>
        Number(Math.Abs(degrees), digits) + "° " + this[degrees < 0 ? "common.dir.w" : "common.dir.e"];

    public static string Number(double value, int digits) =>
        value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    public string Place(string? name)
    {
        if (name is null)
        {
            return string.Empty;
        }

        var key = "place." + name;
        var text = Strings.Get(Language, key);
        return text == key ? name : text;
    }

    public string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.InvariantCulture, this[key], arguments);

    public string Hours(double hours) =>
        hours.ToString("F1", CultureInfo.InvariantCulture) + " " + this["common.unit.h"];

    public string Duration(TimeSpan span) =>
        span.TotalHours >= 48
            ? span.TotalDays.ToString("F1", CultureInfo.InvariantCulture) + " " + this["common.unit.d"]
            : Hours(span.TotalHours);

    public string Span(TimeSpan span)
    {
        var minutes = (int)Math.Round(span.TotalMinutes);
        var days = minutes / 1440;
        var hours = minutes % 1440 / 60;
        var rest = minutes % 60;
        var parts = new List<string>();
        if (days > 0)
        {
            parts.Add(days + " " + this["common.unit.d"]);
        }

        if (hours > 0)
        {
            parts.Add(hours + " " + this["common.unit.h"]);
        }

        if (days == 0 && (rest > 0 || parts.Count == 0))
        {
            parts.Add(rest + " " + this["common.unit.min"]);
        }

        return string.Join(' ', parts);
    }

    public string Profile(string key) =>
        FinderPreset.All.FirstOrDefault(preset => preset.Key == key) is { } found
            ? Format("finder.profileDays", this["finder.profile." + key], found.DurationDays)
            : this["finder.profile." + key];

    public string ShortDate(DateTimeOffset moment)
    {
        var months = Language == Strings.Ukrainian
            ? new[] { "січ.", "лют.", "бер.", "квіт.", "трав.", "черв.", "лип.", "серп.", "вер.", "жовт.", "лист.", "груд." }
            : new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        return moment.Day.ToString(CultureInfo.InvariantCulture) + " " + months[moment.Month - 1] + " " + moment.Year.ToString(CultureInfo.InvariantCulture);
    }

    public string Days(TimeSpan span) =>
        span.TotalDays.ToString("F0", CultureInfo.InvariantCulture) + " " + this["common.unit.d"];

    public async Task InitAsync()
    {
        string? stored = null;
        string browser = Strings.English;
        try
        {
            stored = await js.InvokeAsync<string?>("shadowlineLang.get");
            browser = await js.InvokeAsync<string>("shadowlineLang.browser");
        }
        catch (JSException)
        {
        }

        Language = Strings.IsSupported(stored)
            ? stored!
            : browser.StartsWith("uk", StringComparison.OrdinalIgnoreCase) ? Strings.Ukrainian : Strings.English;
        await ApplyDocumentLanguage();
    }

    public async Task SetAsync(string language)
    {
        if (!Strings.IsSupported(language) || language == Language)
        {
            return;
        }

        Language = language;
        try
        {
            await js.InvokeVoidAsync("shadowlineLang.set", language);
        }
        catch (JSException)
        {
        }

        Changed?.Invoke();
    }

    private async Task ApplyDocumentLanguage()
    {
        try
        {
            await js.InvokeVoidAsync("shadowlineLang.apply", Language);
        }
        catch (JSException)
        {
        }
    }
}
