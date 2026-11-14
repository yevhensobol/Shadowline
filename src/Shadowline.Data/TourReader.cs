namespace Shadowline.Data;

public sealed record TourStep(
    string Id,
    string Site,
    DateTimeOffset? Time,
    int? WindowHours,
    string? Focus,
    string? Link,
    IReadOnlyDictionary<string, string>? Title,
    IReadOnlyDictionary<string, string>? Text,
    string? Scene = null,
    string? Layer = null);

public sealed record TourDocument(int FormatVersion, IReadOnlyList<TourStep>? Steps);

public static class TourReader
{
    public const int SupportedFormatVersion = 1;

    private static readonly string[] RequiredLanguages = ["en", "uk"];

    public static readonly IReadOnlyList<string> Scenes = ["orbit", "stand", "earth", "sun", "eclipse"];

    public static readonly IReadOnlyList<string> Layers = ["none", "sun", "earth", "slope"];

    public static async ValueTask<TourDocument> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var document = await JsonReader.ReadAsync(stream, DataJsonContext.Default.TourDocument, "tour", cancellationToken);
        if (document.FormatVersion != SupportedFormatVersion)
        {
            throw new DataFormatException(
                $"Unsupported tour format version {document.FormatVersion}, expected {SupportedFormatVersion}");
        }

        if (document.Steps is null || document.Steps.Count == 0)
        {
            throw new DataFormatException("Tour has no steps");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in document.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Id) || !ids.Add(step.Id))
            {
                throw new DataFormatException("Tour step has an empty or duplicate id");
            }

            if (string.IsNullOrWhiteSpace(step.Site))
            {
                throw new DataFormatException($"Tour step '{step.Id}' has no site");
            }

            if (step.WindowHours is <= 0)
            {
                throw new DataFormatException($"Tour step '{step.Id}' has a non-positive window");
            }

            if (step.Scene is not null && !Scenes.Contains(step.Scene))
            {
                throw new DataFormatException($"Tour step '{step.Id}' has an unknown scene '{step.Scene}'");
            }

            if (step.Layer is not null && !Layers.Contains(step.Layer))
            {
                throw new DataFormatException($"Tour step '{step.Id}' has an unknown layer '{step.Layer}'");
            }

            RequireText(step.Id, "title", step.Title);
            RequireText(step.Id, "text", step.Text);
        }

        return document;
    }

    private static void RequireText(string id, string name, IReadOnlyDictionary<string, string>? values)
    {
        foreach (var language in RequiredLanguages)
        {
            if (values is null || !values.TryGetValue(language, out var value) || string.IsNullOrWhiteSpace(value))
            {
                throw new DataFormatException($"Tour step '{id}' lacks {name} in '{language}'");
            }
        }
    }
}
