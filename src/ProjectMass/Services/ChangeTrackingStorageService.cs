namespace ProjectMass.Services;

public sealed class LocalSyncChange
{
    public static readonly Guid Key = Guid.Parse("00000000-0000-0000-0000-000000000009");
    public Guid Id { get; init; } = Key;
    public DateTimeOffset At { get; init; }
}

public sealed class ChangeTrackingStorageService(IStorageService storage, TimeProvider clock) : IStorageService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public event Action? Changed;

    internal IStorageService Inner => storage;
    internal Task<LocalSyncChange?> GetChangeAsync() =>
        storage.GetAsync<LocalSyncChange>(StorageStores.SyncMetadata, LocalSyncChange.Key);

    internal async Task<T> ImportLocalAsync<T>(Func<IStorageService, Task<T>> import)
    {
        var result = await WithLocalDataAsync(async () =>
        {
            var imported = await import(storage);
            await StampAsync(clock.GetUtcNow());
            return imported;
        });
        Changed?.Invoke();
        return result;
    }

    public Task<T?> GetAsync<T>(string store, Guid id) => storage.GetAsync<T>(store, id);
    public Task<T?> GetAsync<T>(string store, string id) => storage.GetAsync<T>(store, id);
    public Task<List<T>> GetAllAsync<T>(string store) => storage.GetAllAsync<T>(store);

    public Task PutAsync<T>(string store, T item) =>
        ChangeAsync(store, "put", item, null, () => storage.PutAsync(store, item));
    public Task DeleteAsync(string store, Guid id) =>
        ChangeAsync<object>(store, "delete", null, id, () => storage.DeleteAsync(store, id));
    public Task ClearAsync(string store) =>
        ChangeAsync<object>(store, "clear", null, null, () => storage.ClearAsync(store));

    internal async Task<T> WithLocalDataAsync<T>(Func<Task<T>> work)
    {
        await gate.WaitAsync();
        try { return await work(); }
        finally { gate.Release(); }
    }

    private async Task ChangeAsync<T>(string store, string operation, T? item, Guid? id, Func<Task> mutation)
    {
        if (!StorageStores.TracksChanges(store)) { await mutation(); return; }
        await WithLocalDataAsync(async () =>
        {
            var now = clock.GetUtcNow();
            if (storage is IndexedDbStorageService indexed)
                await indexed.ChangeTrackedAsync(store, operation, item, id, now);
            else
            {
                await mutation();
                await StampAsync(now);
            }
            return true;
        });
        Changed?.Invoke();
    }

    private async Task StampAsync(DateTimeOffset now)
    {
        var previous = (await GetChangeAsync())?.At;
        await storage.PutAsync(StorageStores.SyncMetadata,
            new LocalSyncChange { At = previous >= now ? previous.Value.AddMilliseconds(1) : now });
    }
}
