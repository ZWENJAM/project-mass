using ProjectMass.Models;

namespace ProjectMass.Services;

public sealed class ProfileService(IStorageService storage, INutritionCalculator calculator, TimeProvider clock) : IProfileService
{
    public async Task<UserProfile> GetAsync() => await storage.GetAsync<UserProfile>(StorageStores.UserProfiles, UserProfile.SingletonId) ?? new();

    public async Task SaveAsync(UserProfile profile)
    {
        var year = clock.GetLocalNow().Year;
        if (profile.BirthYear is { } born && (born < year - 120 || born >= year)) throw new ArgumentException("Bitte ein gültiges Geburtsjahr angeben.");
        if (profile.HeightCm is <= 0 or > 300 || profile.CurrentWeightKg is <= 0 or > 1000)
            throw new ArgumentException("Größe und Gewicht müssen positiv sein oder leer bleiben.");
        if (profile.TargetKcal < 0 || profile.TargetProteinGrams < 0 || profile.TargetCarbohydrateGrams < 0 || profile.TargetFatGrams < 0 || profile.TrainingDaysPerWeek is < 0 or > 7)
            throw new ArgumentException("Ziele dürfen nicht negativ sein. Trainingstage: 0–7 pro Woche.");
        if (profile.TargetWaterMl is < 0 or > 10000) throw new ArgumentException("Wasserziel: 0 bis 10.000 ml.");
        profile.Id = UserProfile.SingletonId;
        if (profile.UpperBodyIncreaseKg <= 0 || profile.LowerBodyIncreaseKg <= 0 || profile.UpperBodyIncreaseKg > 100 || profile.LowerBodyIncreaseKg > 100)
            throw new ArgumentException("Gewichtsschritte müssen größer als 0 und höchstens 100 kg sein.");
        await storage.PutAsync(StorageStores.UserProfiles, profile);
    }

    public async Task<decimal?> GetEffectiveWeightAsync(UserProfile profile) =>
        (await storage.GetAllAsync<BodyMeasurement>(StorageStores.BodyMeasurements))
        .Where(m => m.WeightKg > 0).OrderByDescending(m => m.Date).ThenBy(m => m.Id)
        .Select(m => m.WeightKg).FirstOrDefault() ?? profile.CurrentWeightKg;

    public async Task<NutritionTargets> SuggestTargetsAsync(UserProfile profile)
    {
        var weight = await GetEffectiveWeightAsync(profile);
        if (weight is null || profile.HeightCm is null || profile.BirthYear is null)
            throw new ArgumentException("Bitte Geburtsjahr, Größe und aktuelles Gewicht angeben.");
        return calculator.CalculateTargets(profile.Gender, weight.Value, profile.HeightCm.Value,
            clock.GetLocalNow().Year - profile.BirthYear.Value, profile.ActivityLevel, profile.Goal);
    }
}
