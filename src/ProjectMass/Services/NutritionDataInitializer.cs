using System.Net.Http.Json;
using ProjectMass.Models;

namespace ProjectMass.Services;

public sealed class NutritionDataInitializer(HttpClient http, IStorageService storage)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool initialized;
    private static readonly Guid MarkerId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    public async Task InitializeAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (initialized) return;
            if (await storage.GetAsync<TrainingDataInitializer.SeedMarker>(StorageStores.AppMetadata, MarkerId) is null)
            {
                var snacks = await http.GetFromJsonAsync<List<FoodItem>>("data/snacks.json") ?? throw new InvalidOperationException("Snack-Startdaten fehlen.");
                foreach (var food in snacks)
                    if (await storage.GetAsync<FoodItem>(StorageStores.FoodItems, food.Id) is null)
                        await storage.PutAsync(StorageStores.FoodItems, food);
                await storage.PutAsync(StorageStores.AppMetadata, new TrainingDataInitializer.SeedMarker { Id = MarkerId });
            }
            initialized = true;
        }
        finally { gate.Release(); }
    }
}
