namespace Shadowline.Data;

public sealed record ExternalLink(IReadOnlyDictionary<string, string>? Title, IReadOnlyDictionary<string, string>? Url)
{
    private static readonly string[] RequiredLanguages = ["en", "uk"];

    public string TitleIn(string language) => Pick(Title, language);

    public string UrlIn(string language) => Pick(Url, language);

    internal static void Validate(string owner, IReadOnlyList<ExternalLink>? links)
    {
        if (links is null)
        {
            return;
        }

        foreach (var link in links)
        {
            foreach (var language in RequiredLanguages)
            {
                if (link.Title is null || !link.Title.TryGetValue(language, out var title) || string.IsNullOrWhiteSpace(title))
                {
                    throw new DataFormatException($"Link in '{owner}' has no title in '{language}'");
                }
            }

            if (link.Url is null || !link.Url.ContainsKey("en"))
            {
                throw new DataFormatException($"Link in '{owner}' has no English url");
            }

            foreach (var url in link.Url.Values)
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                {
                    throw new DataFormatException($"Link in '{owner}' is not an https url");
                }
            }
        }
    }

    private static string Pick(IReadOnlyDictionary<string, string>? values, string language) =>
        values is null ? string.Empty
        : values.TryGetValue(language, out var text) ? text
        : values.GetValueOrDefault("en", string.Empty);
}
