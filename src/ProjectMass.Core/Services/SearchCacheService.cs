using ProjectMass.Models;

namespace ProjectMass.Services;

public sealed class FoodSearchCacheEntry
{
    public string Id { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public List<FoodItem> Foods { get; set; } = [];
}

public sealed class SearchCacheService(IStorageService storage, TimeProvider clock)
{
    public static string Key(FoodSource source, string query) => $"v2:{source}:{SearchRequestPolicy.Normalize(query)}";
    public async Task<List<FoodItem>?> GetAsync(FoodSource source, string query)
    {
        var entry = await storage.GetAsync<FoodSearchCacheEntry>(StorageStores.SearchCache, Key(source, query));
        return entry is not null && entry.ExpiresAt > clock.GetUtcNow() ? entry.Foods : null;
    }
    public Task PutAsync(FoodSource source, string query, List<FoodItem> foods) => storage.PutAsync(StorageStores.SearchCache,
        new FoodSearchCacheEntry { Id = Key(source, query), ExpiresAt = clock.GetUtcNow().AddDays(7), Foods = foods });
}
