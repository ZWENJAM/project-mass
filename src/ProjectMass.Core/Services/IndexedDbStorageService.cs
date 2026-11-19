using Microsoft.JSInterop;

namespace ProjectMass.Services;

public sealed class IndexedDbStorageService(IJSRuntime jsRuntime) : IStorageService, IAsyncDisposable
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/db.js").AsTask());

    public async Task<T?> GetAsync<T>(string store, Guid id) =>
        await (await module.Value).InvokeAsync<T?>("get", store, id);

    public async Task<T?> GetAsync<T>(string store, string id) =>
        await (await module.Value).InvokeAsync<T?>("get", store, id);

    public async Task<List<T>> GetAllAsync<T>(string store) =>
        await (await module.Value).InvokeAsync<List<T>>("getAll", store);

    public async Task PutAsync<T>(string store, T item) =>
        await (await module.Value).InvokeVoidAsync("put", store, item);

    public async Task DeleteAsync(string store, Guid id) =>
        await (await module.Value).InvokeVoidAsync("delete", store, id);

    public async Task ClearAsync(string store) =>
        await (await module.Value).InvokeVoidAsync("clear", store);

    internal async Task ChangeTrackedAsync<T>(string store, string operation, T? item, Guid? id, DateTimeOffset at) =>
        await (await module.Value).InvokeVoidAsync("changeTracked", store, operation, item, id, at);

    internal async Task<bool> ReplaceForSyncAsync(BackupData data, DateTimeOffset? expectedChange) =>
        await (await module.Value).InvokeAsync<bool>("replaceForSync", data, expectedChange);

    internal async Task MarkSyncBackupAsync(Guid id, DateTimeOffset at) =>
        await (await module.Value).InvokeVoidAsync("markSyncBackup", id, at);

    internal async Task<bool> IsEmptyAsync(string[] stores) =>
        await (await module.Value).InvokeAsync<bool>("isEmpty", (object)stores);

    internal async Task SeedIfEmptyAsync(string[] stores, Dictionary<string, IEnumerable<object>> seeds) =>
        await (await module.Value).InvokeVoidAsync("seedIfEmpty", stores, seeds);

    public async ValueTask DisposeAsync()
    {
        if (module.IsValueCreated)
        {
            try { await (await module.Value).DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
    }
}
