using System.Net.Http.Json;
using ProjectMass.Models;

namespace ProjectMass.Services;

public sealed class TrainingDataInitializer(HttpClient http, IStorageService storage)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool initialized;
    private static readonly Guid MarkerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const int CurrentVersion = 2;

    public async Task InitializeAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (initialized) return;
            var marker = await storage.GetAsync<SeedMarker>(StorageStores.AppMetadata, MarkerId);
            if (marker is null || marker.Version < CurrentVersion)
            {
                var exercises = await http.GetFromJsonAsync<List<Exercise>>("data/exercises.json") ?? throw new InvalidOperationException("Übungsdaten fehlen.");
                foreach (var seed in exercises)
                {
                    var existing = await storage.GetAsync<Exercise>(StorageStores.Exercises, seed.Id);
                    if (existing is null && marker is null)
                        await storage.PutAsync(StorageStores.Exercises, seed);
                    else if (existing is not null && ExerciseGuidance.FillMissing(existing, seed))
                        await storage.PutAsync(StorageStores.Exercises, existing);
                }
                if (marker is null)
                {
                    var plans = await http.GetFromJsonAsync<List<WorkoutPlan>>("data/workout-plans.json") ?? throw new InvalidOperationException("Startpläne fehlen.");
                    foreach (var plan in plans)
                        if (await storage.GetAsync<WorkoutPlan>(StorageStores.WorkoutPlans, plan.Id) is null)
                            await storage.PutAsync(StorageStores.WorkoutPlans, plan);
                }
                await storage.PutAsync(StorageStores.AppMetadata, new SeedMarker { Id = MarkerId, Version = CurrentVersion });
            }
            initialized = true;
        }
        finally { gate.Release(); }
    }

    public sealed class SeedMarker
    {
        public Guid Id { get; set; }
        public int Version { get; set; } = 1;
    }
}
