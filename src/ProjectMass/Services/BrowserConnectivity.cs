using Microsoft.JSInterop;

namespace ProjectMass.Services;

public interface IBrowserConnectivity { Task<bool> IsOnlineAsync(); }
public sealed class BrowserConnectivity(IJSRuntime js) : IBrowserConnectivity, IAsyncDisposable
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() => js.InvokeAsync<IJSObjectReference>("import", "./js/foodSearch.js").AsTask());
    public async Task<bool> IsOnlineAsync() => await (await module.Value).InvokeAsync<bool>("isOnline");
    public async ValueTask DisposeAsync() { if (module.IsValueCreated) await (await module.Value).DisposeAsync(); }
}
