using ProjectMass.Models;

namespace ProjectMass.Services;

public interface IMeasurementService
{
    Task<List<BodyMeasurement>> GetAllAsync();
    Task SaveAsync(BodyMeasurement measurement);
    Task DeleteAsync(Guid id);
}
public sealed class MeasurementService(IStorageService storage) : IMeasurementService
{
    public async Task<List<BodyMeasurement>> GetAllAsync() => (await storage.GetAllAsync<BodyMeasurement>(StorageStores.BodyMeasurements)).OrderByDescending(m => m.Date).ThenBy(m => m.Id).ToList();
    public async Task SaveAsync(BodyMeasurement measurement)
    {
        var values = new[] { measurement.WeightKg, measurement.WaistCm, measurement.ChestCm, measurement.UpperArmCm, measurement.BodyFatPercent };
        if (values.All(v => v is null)) throw new ArgumentException("Bitte mindestens einen Messwert eintragen.");
        if (values.Any(v => v <= 0) || measurement.BodyFatPercent > 100) throw new ArgumentException("Messwerte müssen positiv sein; KFA darf höchstens 100 % betragen.");
        await storage.PutAsync(StorageStores.BodyMeasurements, measurement);
    }
    public Task DeleteAsync(Guid id) => storage.DeleteAsync(StorageStores.BodyMeasurements, id);
}
