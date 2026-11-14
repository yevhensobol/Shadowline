using Shadowline.Core.Finder;
using Shadowline.Data;

namespace Shadowline.Web.Services;

public sealed class FinderStore(HttpClient http)
{
    private Task<FinderGrid?>? _loading;

    public FinderGrid? Grid { get; private set; }

    public Task<FinderGrid?> LoadAsync() => _loading ??= LoadCoreAsync();

    private async Task<FinderGrid?> LoadCoreAsync()
    {
        try
        {
            var bytes = await http.GetByteArrayAsync("data/finder.bin.gz");
            Grid = bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b ? FinderReader.ReadGzip(bytes) : FinderReader.Read(bytes);
            return Grid;
        }
        catch (Exception exception) when (exception is HttpRequestException or DataFormatException or InvalidDataException)
        {
            _loading = null;
            return null;
        }
    }
}
