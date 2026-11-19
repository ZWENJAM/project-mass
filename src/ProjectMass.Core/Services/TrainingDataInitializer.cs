using System.Net.Http.Json;
using ProjectMass.Models;

namespace ProjectMass.Services;

public sealed class TrainingDataInitializer(HttpClient http, IStorageService storage)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool initialized;
    private static readonly SeedMarker[] Markers =
    [
        new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), Version = 2 },
        new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000002") }
    ];
    private static readonly string[] PersistentStores =
    [
        StorageStores.UserProfiles, StorageStores.Exercises, StorageStores.WorkoutPlans,
        StorageStores.WorkoutSessions, StorageStores.FoodItems, StorageStores.FoodLogEntries,
        StorageStores.Recipes, StorageStores.BodyMeasurements, StorageStores.WaterLogs,
        StorageStores.Notes, StorageStores.AppMetadata, StorageStores.SyncMetadata, StorageStores.SearchCache
    ];

    public async Task InitializeAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (initialized) return;
            if (storage is IndexedDbStorageService indexed)
            {
                if (await indexed.IsEmptyAsync(PersistentStores))
                {
                    var seeds = await LoadSeedsAsync();
                    await indexed.SeedIfEmptyAsync(PersistentStores, seeds);
                }
            }
            else if (await IsEmptyAsync())
            {
                var seeds = await LoadSeedsAsync();
                if (await IsEmptyAsync())
                    foreach (var (store, items) in seeds)
                        foreach (var item in items)
                            await storage.PutAsync(store, item);
            }
            initialized = true;
        }
        finally { gate.Release(); }
    }

    private async Task<bool> IsEmptyAsync()
    {
        foreach (var store in PersistentStores)
            if ((await storage.GetAllAsync<object>(store)).Count > 0) return false;
        return true;
    }

    private async Task<Dictionary<string, IEnumerable<object>>> LoadSeedsAsync() => new()
    {
        [StorageStores.Exercises] = await ReadAsync<Exercise>("exercises.json"),
        [StorageStores.WorkoutPlans] = await ReadAsync<WorkoutPlan>("workout-plans.json"),
        [StorageStores.FoodItems] = await ReadAsync<FoodItem>("snacks.json"),
        [StorageStores.AppMetadata] = Markers
    };

    private async Task<List<T>> ReadAsync<T>(string file) =>
        await http.GetFromJsonAsync<List<T>>("data/" + file)
        ?? throw new InvalidOperationException($"Startdaten fehlen: {file}");

    public sealed class SeedMarker
    {
        public Guid Id { get; set; }
        public int Version { get; set; } = 1;
    }
}
