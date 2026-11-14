using System.Text;
using Shadowline.Data;

namespace Shadowline.Tests;

public class YearsReaderTests
{
    private static MemoryStream Json(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task Reads_years_and_rejects_unsafe_files()
    {
        const string good = """{"formatVersion":1,"years":[{"id":"2026","gridStartUtc":"2026-11-01T00:00:00Z","gridStepMinutes":30,"gridCount":17520,"file":"ephemeris.bin"},{"id":"2027","gridStartUtc":"2027-11-01T00:00:00Z","gridStepMinutes":30,"gridCount":17568,"file":"years/2027.bin"}]}""";
        var document = await YearsReader.ReadAsync(Json(good));
        Assert.Equal(2, document.Years!.Count);
        Assert.Equal(17568, document.Years[1].Grid.Count);

        await Assert.ThrowsAsync<DataFormatException>(() => YearsReader.ReadAsync(Json(good.Replace("years/2027.bin", "../x.bin"))).AsTask());
        await Assert.ThrowsAsync<DataFormatException>(() => YearsReader.ReadAsync(Json(good.Replace("\"2027\"", "\"2026\""))).AsTask());
    }
}
