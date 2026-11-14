using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using Shadowline.Core.Finder;
using Shadowline.Core.Missions;

namespace Shadowline.Web.Services;

public sealed class MissionStore
{
    private const string StorageKey = "mission";

    private readonly IJSRuntime js;
    private readonly NavigationManager navigation;
    private bool loaded;

    public MissionStore(IJSRuntime js, NavigationManager navigation)
    {
        this.js = js;
        this.navigation = navigation;
        navigation.LocationChanged += OnLocationChanged;
    }

    public static Mission Initial { get; } = FinderPreset.Water.Apply(Mission.Default);

    public Mission Current { get; private set; } = Initial;

    public event Action? Changed;

    public async Task EnsureLoadedAsync()
    {
        if (loaded)
        {
            return;
        }

        loaded = true;
        if (FromUrl() is { } linked)
        {
            Current = linked;
            await SaveAsync();
            return;
        }

        await LoadStoredAsync();
        SyncUrl();
    }

    private async Task LoadStoredAsync()
    {
        try
        {
            if (Mission.ParseQueryValue(await js.InvokeAsync<string?>("shadowlineHint.get", StorageKey)) is { } stored)
            {
                Current = stored;
            }
        }
        catch (JSException)
        {
        }
    }

    public async Task SetAsync(Mission mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        var next = mission.Clamp();
        if (next == Current)
        {
            return;
        }

        Current = next;
        Changed?.Invoke();
        SyncUrl();
        await SaveAsync();
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        if (loaded)
        {
            SyncUrl();
        }
    }

    private void SyncUrl()
    {
        var uri = new Uri(navigation.Uri);
        var parts = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Where(part => !part.StartsWith("m=", StringComparison.Ordinal)).ToList();
        if (Current != Initial)
        {
            parts.Add(Query);
        }

        var next = uri.GetLeftPart(UriPartial.Path) + (parts.Count > 0 ? "?" + string.Join('&', parts) : string.Empty) + uri.Fragment;
        if (next != navigation.Uri)
        {
            navigation.NavigateTo(next, new NavigationOptions { ReplaceHistoryEntry = true });
        }
    }

    public string Query => "m=" + Uri.EscapeDataString(Current.ToQueryValue());

    public string QuerySuffix => Current == Initial ? string.Empty : "&" + Query;

    private Mission? FromUrl()
    {
        foreach (var pair in new Uri(navigation.Uri).Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == "m")
            {
                return Mission.ParseQueryValue(Uri.UnescapeDataString(parts[1]));
            }
        }

        return null;
    }

    private async Task SaveAsync()
    {
        try
        {
            await js.InvokeVoidAsync("shadowlineHint.set", StorageKey, Current.ToQueryValue());
        }
        catch (JSException)
        {
        }
    }
}
