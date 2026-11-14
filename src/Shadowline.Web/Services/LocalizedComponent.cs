using Microsoft.AspNetCore.Components;

namespace Shadowline.Web.Services;

public abstract class LocalizedComponent : ComponentBase, IDisposable
{
    [Inject]
    protected Localizer L { get; set; } = default!;

    protected override void OnInitialized()
    {
        L.Changed += OnLanguageChanged;
    }

    public virtual void Dispose()
    {
        L.Changed -= OnLanguageChanged;
        GC.SuppressFinalize(this);
    }

    private void OnLanguageChanged() => _ = InvokeAsync(StateHasChanged);
}
