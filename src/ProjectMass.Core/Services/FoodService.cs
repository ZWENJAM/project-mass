using ProjectMass.Models;

namespace ProjectMass.Services;

public sealed class FoodService(IStorageService storage) : IFoodService
{
    private readonly SemaphoreSlim importGate = new(1, 1);
    public Task<FoodItem?> GetAsync(Guid id) => storage.GetAsync<FoodItem>(StorageStores.FoodItems, id);
    public async Task<List<FoodItem>> SearchAsync(string? query = null, bool favoritesOnly = false) =>
        (await storage.GetAllAsync<FoodItem>(StorageStores.FoodItems))
        .Where(f => (!favoritesOnly || f.IsFavorite) && (string.IsNullOrWhiteSpace(query) ||
            $"{f.Name} {f.Brand}".Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase)))
        .OrderBy(f => f.Name).ToList();

    public async Task<List<FoodItem>> GetRecentAsync()
    {
        var foods = (await storage.GetAllAsync<FoodItem>(StorageStores.FoodItems)).ToDictionary(f => f.Id);
        return (await storage.GetAllAsync<FoodLogEntry>(StorageStores.FoodLogEntries))
            .OrderByDescending(e => e.LoggedAt).ThenByDescending(e => e.Date)
            .Select(e => e.FoodSnapshot.Id).Distinct().Where(foods.ContainsKey).Take(15).Select(id => foods[id]).ToList();
    }

    public async Task SaveAsync(FoodItem food)
    {
        if (string.IsNullOrWhiteSpace(food.Name) || food.Name.Trim().Length > 180) throw new ArgumentException("Bitte einen Namen mit 1–180 Zeichen angeben.");
        if (food.HasIncompleteNutrients) throw new ArgumentException("Bitte die fehlenden Kalorien pro 100 g manuell ergänzen.");
        var n = food.NutrientsPer100G;
        if (new[] { n.Kcal, n.ProteinGrams, n.CarbohydrateGrams, n.FatGrams, n.FiberGrams, n.SugarGrams, n.SaltGrams }.Any(v => v < 0))
            throw new ArgumentException("Nährwerte dürfen nicht negativ sein.");
        if (food.PortionSizes.Any(p => string.IsNullOrWhiteSpace(p.Name) || p.Grams <= 0)) throw new ArgumentException("Jede Portion benötigt einen Namen und ein positives Gewicht in g.");
        food.Name = food.Name.Trim();
        await storage.PutAsync(StorageStores.FoodItems, food);
    }
    public Task DeleteAsync(Guid id) => storage.DeleteAsync(StorageStores.FoodItems, id);
    public async Task<FoodItem> ImportAsync(FoodItem food)
    {
        await importGate.WaitAsync();
        try
        {
            if (food.HasIncompleteNutrients) throw new ArgumentException("Bitte zuerst die fehlenden Kalorien pro 100 g ergänzen.");
            var existing = (await storage.GetAllAsync<FoodItem>(StorageStores.FoodItems)).FirstOrDefault(f =>
                (!string.IsNullOrWhiteSpace(food.Barcode) && !string.IsNullOrWhiteSpace(f.Barcode) && BarcodeValidator.Canonical(f.Barcode) == BarcodeValidator.Canonical(food.Barcode)) ||
                (f.Source == food.Source && !string.IsNullOrWhiteSpace(food.ExternalId) && f.ExternalId == food.ExternalId));
            // Keep local corrections, portions and favorites when importing the same product again.
            if (existing is not null) return existing;
            food.Id = Guid.NewGuid();
            await SaveAsync(food);
            return food;
        }
        finally { importGate.Release(); }
    }
    public async Task SetFavoriteAsync(Guid id, bool favorite)
    {
        var food = await GetAsync(id) ?? throw new ArgumentException("Lebensmittel nicht gefunden.");
        food.IsFavorite = favorite;
        await storage.PutAsync(StorageStores.FoodItems, food);
    }
}
