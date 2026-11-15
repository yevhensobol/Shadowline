using System.Text;
using Shadowline.Data;

namespace Shadowline.Tests;

public class CreditsReaderTests
{
    private static MemoryStream Json(string text) => new(Encoding.UTF8.GetBytes(text));

    private const string ValidEntry = """
        {"id":"a","title":"T","author":"A","source":"https://example.org","license":"L","usedIn":{"en":"x","uk":"ікс"}}
        """;

    [Fact]
    public async Task Reads_valid_credits()
    {
        var document = await CreditsReader.ReadAsync(Json("{\"formatVersion\":1,\"entries\":[" + ValidEntry + "]}"));

        var entry = Assert.Single(document.Entries!);
        Assert.Equal("ікс", entry.UsedIn!["uk"]);
    }

    [Fact]
    public async Task Insecure_source_and_missing_language_are_rejected()
    {
        var http = ValidEntry.Replace("https://", "http://");
        var noUk = ValidEntry.Replace("\"uk\":\"ікс\"", "\"de\":\"x\"");

        await Assert.ThrowsAsync<DataFormatException>(() => CreditsReader.ReadAsync(Json("{\"formatVersion\":1,\"entries\":[" + http + "]}")).AsTask());
        await Assert.ThrowsAsync<DataFormatException>(() => CreditsReader.ReadAsync(Json("{\"formatVersion\":1,\"entries\":[" + noUk + "]}")).AsTask());
    }

    [Fact]
    public async Task Duplicate_ids_and_empty_lists_are_rejected()
    {
        await Assert.ThrowsAsync<DataFormatException>(() => CreditsReader.ReadAsync(Json("{\"formatVersion\":1,\"entries\":[" + ValidEntry + "," + ValidEntry + "]}")).AsTask());
        await Assert.ThrowsAsync<DataFormatException>(() => CreditsReader.ReadAsync(Json("{\"formatVersion\":1,\"entries\":[]}")).AsTask());
    }

    [Fact]
    public async Task Kind_is_optional_and_checked()
    {
        var software = ValidEntry.Replace("\"license\":\"L\"", "\"license\":\"L\",\"kind\":\"software\"");
        var unknown = ValidEntry.Replace("\"license\":\"L\"", "\"license\":\"L\",\"kind\":\"misc\"");

        var document = await CreditsReader.ReadAsync(Json("{\"formatVersion\":1,\"entries\":[" + software + "]}"));

        Assert.Equal("software", Assert.Single(document.Entries!).Kind);
        Assert.Null((await CreditsReader.ReadAsync(Json("{\"formatVersion\":1,\"entries\":[" + ValidEntry + "]}"))).Entries![0].Kind);
        await Assert.ThrowsAsync<DataFormatException>(() => CreditsReader.ReadAsync(Json("{\"formatVersion\":1,\"entries\":[" + unknown + "]}")).AsTask());
    }
}
