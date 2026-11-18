using ProjectMass.Models;
using ProjectMass.Services;

namespace ProjectMass.Tests;

public class ProfileServiceTests
{
    [Fact]
    public async Task ProfileUsesSingletonAndLatestMeasurementWithFallback()
    {
        var storage = new InMemoryStorageService();
        var service = new ProfileService(storage, new NutritionCalculator(), new FixedClock());
        var profile = await service.GetAsync();
        Assert.Equal(UserProfile.SingletonId, profile.Id);
        profile.Id = Guid.NewGuid(); profile.CurrentWeightKg = 80; profile.HeightCm = 180; profile.BirthYear = 1996; profile.Gender = Gender.Male;
        await service.SaveAsync(profile);
        Assert.Single(await storage.GetAllAsync<UserProfile>(StorageStores.UserProfiles));
        Assert.Equal(UserProfile.SingletonId, (await service.GetAsync()).Id);
        Assert.Equal(80m, await service.GetEffectiveWeightAsync(profile));
        Assert.Equal(1780, (await service.SuggestTargetsAsync(profile)).Bmr);
        await storage.PutAsync(StorageStores.BodyMeasurements, new BodyMeasurement { Date = new(2026, 11, 1), WeightKg = 90 });
        await storage.PutAsync(StorageStores.BodyMeasurements, new BodyMeasurement { Date = new(2026, 11, 15), WeightKg = 85 });
        await storage.PutAsync(StorageStores.BodyMeasurements, new BodyMeasurement { Date = new(2026, 11, 16), WaistCm = 90 });
        Assert.Equal(85m, await service.GetEffectiveWeightAsync(profile));
        Assert.Equal(1830m, (await service.SuggestTargetsAsync(profile)).Bmr);
        profile.TargetKcal = 2222; profile.TargetCarbohydrateGrams = 100.125m;
        await service.SaveAsync(profile);
        Assert.Equal(2222m, (await service.GetAsync()).TargetKcal);
        Assert.Equal(100.125m, (await service.GetAsync()).TargetCarbohydrateGrams);
    }
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    }
}
