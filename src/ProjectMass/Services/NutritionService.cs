using System.Text.Json;
using ProjectMass.Models;

namespace ProjectMass.Services;

public sealed class NutritionService(IStorageService storage, IFoodService foods, INutritionCalculator calculator, TimeProvider clock) : INutritionService
{
    public async Task<NutritionDay> GetDayAsync(DateOnly date)
    {
        var entries = (await storage.GetAllAsync<FoodLogEntry>(StorageStores.FoodLogEntries)).Where(e => e.Date == date).OrderBy(e => e.LoggedAt).ToList();
        var meals = Enum.GetValues<MealType>().Select(meal =>
        {
            var items = entries.Where(e => e.Meal == meal).ToList();
            return new MealSummary(meal, items, calculator.Sum(items.Select(e => calculator.ForAmount(e.FoodSnapshot.NutrientsPer100G, e.AmountGrams))));
        }).ToList();
        return new(date, meals, calculator.Sum(meals.Select(m => m.Total)));
    }
    public Task<FoodLogEntry?> GetAsync(Guid id) => storage.GetAsync<FoodLogEntry>(StorageStores.FoodLogEntries, id);

    public async Task AddAsync(DateOnly date, MealType meal, Guid foodId, decimal grams)
    {
        var food = await foods.GetAsync(foodId) ?? throw new ArgumentException("Lebensmittel nicht gefunden.");
        if (food.HasIncompleteNutrients) throw new ArgumentException("Bitte zuerst die fehlenden Kalorien ergänzen.");
        Validate(meal, grams);
        await storage.PutAsync(StorageStores.FoodLogEntries, new FoodLogEntry
        {
            Date = date, Meal = meal, FoodSnapshot = Clone(food), AmountGrams = grams, LoggedAt = clock.GetUtcNow()
        });
    }

    public async Task UpdateAsync(FoodLogEntry entry)
    {
        Validate(entry.Meal, entry.AmountGrams);
        var original = await GetAsync(entry.Id) ?? throw new ArgumentException("Eintrag nicht gefunden.");
        if (original.IsQuickEntry) throw new ArgumentException("Schnelleinträge werden über Kalorien und Protein bearbeitet.");
        // Editing amount/meal/date must never replace the historical food snapshot.
        original.Date = entry.Date; original.Meal = entry.Meal; original.AmountGrams = entry.AmountGrams;
        await storage.PutAsync(StorageStores.FoodLogEntries, original);
    }

    public async Task SaveQuickEntryAsync(DateOnly date, MealType meal, string? name, decimal? kcal, decimal? protein, Guid? id = null)
    {
        if (!Enum.IsDefined(meal)) throw new ArgumentException("Ungültige Mahlzeit.");
        if (kcal is null or < 0 or > 100_000 || protein is null or < 0 or > 10_000)
            throw new ArgumentException("Bitte 0–100.000 kcal und 0–10.000 g Protein angeben.");
        name = name?.Trim();
        if (name?.Length > 120) throw new ArgumentException("Der Name darf höchstens 120 Zeichen lang sein.");

        var entry = id.HasValue
            ? await GetAsync(id.Value) ?? throw new ArgumentException("Eintrag nicht gefunden.")
            : new FoodLogEntry { IsQuickEntry = true, LoggedAt = clock.GetUtcNow() };
        if (!entry.IsQuickEntry) throw new ArgumentException("Dieser Eintrag ist kein Schnelleintrag.");
        entry.Date = date;
        entry.Meal = meal;
        // The owned snapshot represents the whole entry. The standard 100 g calculation returns its totals.
        entry.AmountGrams = 100;
        entry.FoodSnapshot.Name = string.IsNullOrWhiteSpace(name) ? "Schnelleintrag" : name;
        entry.FoodSnapshot.NutrientsPer100G = new Nutrients { Kcal = kcal.Value, ProteinGrams = protein.Value };
        await storage.PutAsync(StorageStores.FoodLogEntries, entry);
    }
    public Task DeleteAsync(Guid id) => storage.DeleteAsync(StorageStores.FoodLogEntries, id);

    public async Task<int> CopyPreviousMealAsync(DateOnly date, MealType meal)
    {
        if (!Enum.IsDefined(meal)) throw new ArgumentException("Ungültige Mahlzeit.");
        var previous = (await GetDayAsync(date.AddDays(-1))).Meals.Single(m => m.Meal == meal).Entries;
        foreach (var source in previous)
        {
            var copy = Clone(source);
            copy.Id = Guid.NewGuid(); copy.Date = date; copy.LoggedAt = clock.GetUtcNow();
            await storage.PutAsync(StorageStores.FoodLogEntries, copy);
        }
        return previous.Count;
    }

    public decimal PortionGrams(PortionSize portion, decimal count)
    {
        if (count <= 0 || portion.Grams <= 0) throw new ArgumentException("Portionsanzahl und Portionsgewicht müssen positiv sein.");
        return portion.Grams * count;
    }
    private static void Validate(MealType meal, decimal grams)
    {
        if (!Enum.IsDefined(meal) || grams <= 0) throw new ArgumentException("Bitte eine Mahlzeit und eine positive Menge angeben.");
    }
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
