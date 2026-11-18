using System.Text.Json;
using ProjectMass.Services;

namespace ProjectMass.Tests;

// JSON snapshots mimic the value semantics and camelCase boundary of IndexedDB interop.
public sealed class InMemoryStorageService : IStorageService
{
    private readonly Dictionary<string, Dictionary<string, string>> stores = [];
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public bool FailWrites { get; set; }
    private Dictionary<string, string> Store(string name)
    {
        if (!stores.TryGetValue(name, out var store)) stores[name] = store = [];
        return store;
    }
    public Task<T?> GetAsync<T>(string store, Guid id) => GetAsync<T>(store, id.ToString());
    public Task<T?> GetAsync<T>(string store, string id) => Task.FromResult(Store(store).TryGetValue(id, out var json) ? JsonSerializer.Deserialize<T>(json, Options) : default);
    public Task<List<T>> GetAllAsync<T>(string store) => Task.FromResult(Store(store).Values.Select(json => JsonSerializer.Deserialize<T>(json, Options)!).ToList());
    public Task PutAsync<T>(string store, T item)
    {
        if (FailWrites) throw new InvalidOperationException("Simulierter Schreibfehler");
        var json = JsonSerializer.Serialize(item, Options);
        using var document = JsonDocument.Parse(json);
        var id = document.RootElement.GetProperty("id").GetString()!;
        Store(store)[id] = json;
        return Task.CompletedTask;
    }
    public Task DeleteAsync(string store, Guid id) { Store(store).Remove(id.ToString()); return Task.CompletedTask; }
    public Task ClearAsync(string store) { Store(store).Clear(); return Task.CompletedTask; }
}
