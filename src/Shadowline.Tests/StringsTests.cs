using System.Text.RegularExpressions;
using Shadowline.Core.Localization;

namespace Shadowline.Tests;

public class StringsTests
{
    [Fact]
    public void Every_key_has_both_languages()
    {
        foreach (var key in Strings.Keys)
        {
            Assert.True(Strings.Get(Strings.English, key).Length > 0);
            Assert.True(Strings.Get(Strings.Ukrainian, key).Length > 0);
        }
    }

    [Fact]
    public void Translations_differ_from_english_for_text_keys()
    {
        var same = Strings.Keys
            .Where(key => Strings.Get(Strings.English, key) == Strings.Get(Strings.Ukrainian, key))
            .ToArray();

        Assert.Empty(same);
    }

    [Fact]
    public void Keys_are_not_declared_twice()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Shadowline.Core", "Localization", "Strings.cs")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Shadowline.Core", "Localization", "Strings.cs"));
        var keys = System.Text.RegularExpressions.Regex.Matches(source, @"^\s*\[""([^""]+)""\] =", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .ToList();

        Assert.Empty(keys.GroupBy(key => key).Where(group => group.Count() > 1).Select(group => group.Key));
    }

    [Fact]
    public void Placeholders_match_between_languages()
    {
        foreach (var key in Strings.Keys)
        {
            Assert.Equal(Placeholders(Strings.Get(Strings.English, key)), Placeholders(Strings.Get(Strings.Ukrainian, key)));
        }
    }

    [Fact]
    public void Month_names_have_twelve_entries()
    {
        Assert.Equal(12, Strings.Get(Strings.English, "windows.months").Split(',').Length);
        Assert.Equal(12, Strings.Get(Strings.Ukrainian, "windows.months").Split(',').Length);
    }

    [Fact]
    public void Unknown_key_returns_the_key()
    {
        Assert.Equal("no.such.key", Strings.Get(Strings.English, "no.such.key"));
    }

    [Fact]
    public void Unknown_language_falls_back_to_english()
    {
        Assert.Equal(Strings.Get(Strings.English, "common.back"), Strings.Get("de", "common.back"));
    }

    [Fact]
    public void Supported_languages_are_english_and_ukrainian()
    {
        Assert.True(Strings.IsSupported("en"));
        Assert.True(Strings.IsSupported("uk"));
        Assert.False(Strings.IsSupported("ru"));
        Assert.False(Strings.IsSupported(null));
    }

    private static string[] Placeholders(string text) =>
        Regex.Matches(text, @"\{\d+\}").Select(match => match.Value).OrderBy(value => value).ToArray();
}
