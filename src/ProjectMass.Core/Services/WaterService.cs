using ProjectMass.Models;

namespace ProjectMass.Services;

public interface IWaterService
{
    Task<int> GetTotalMlAsync(DateOnly date);
    Task AddAsync(DateOnly date, int amountMl);
    Task<bool> RemoveLastAsync(DateOnly date);
}

public sealed class WaterService(IStorageService storage, TimeProvider clock) : IWaterService
{
    private async Task<List<WaterLog>> DayAsync(DateOnly date) =>
        (await storage.GetAllAsync<WaterLog>(StorageStores.WaterLogs)).Where(w => w.Date == date).ToList();

    public async Task<int> GetTotalMlAsync(DateOnly date) => (await DayAsync(date)).Sum(w => w.AmountMl);

    public async Task AddAsync(DateOnly date, int amountMl)
    {
        if (amountMl is < 1 or > 5000) throw new ArgumentException("Bitte eine Menge von 1 bis 5000 ml angeben.");
        await storage.PutAsync(StorageStores.WaterLogs, new WaterLog { Date = date, AmountMl = amountMl, LoggedAt = clock.GetUtcNow() });
    }

    // Undo for a mis-tap: removes the most recent entry of that day.
    public async Task<bool> RemoveLastAsync(DateOnly date)
    {
        var last = (await DayAsync(date)).OrderByDescending(w => w.LoggedAt).FirstOrDefault();
        if (last is null) return false;
        await storage.DeleteAsync(StorageStores.WaterLogs, last.Id);
        return true;
    }
}
