using ProjectMass.Models;

namespace ProjectMass.Services;

public sealed record RecipeNutrition(Nutrients Total, Nutrients PerPortion, decimal TotalGrams, decimal GramsPerPortion);

public interface IRecipeService
{
    Task<List<Recipe>> GetAllAsync();
    Task<Recipe?> GetAsync(Guid id);
    RecipeNutrition Calculate(Recipe recipe);
    Task<FoodItem> SaveAsync(Recipe recipe);
    Task DeleteAsync(Guid id);
}

// A saved recipe is mirrored as a food item with the same ID. Logging a recipe therefore uses the normal food flow.
public sealed class RecipeService(IStorageService storage, INutritionCalculator calculator) : IRecipeService
{
    public async Task<List<Recipe>> GetAllAsync() =>
        (await storage.GetAllAsync<Recipe>(StorageStores.Recipes)).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    public Task<Recipe?> GetAsync(Guid id) => storage.GetAsync<Recipe>(StorageStores.Recipes, id);

    public RecipeNutrition Calculate(Recipe recipe)
    {
        var total = calculator.Sum(recipe.Ingredients.Select(i => calculator.ForAmount(i.FoodSnapshot.NutrientsPer100G, i.AmountGrams)));
        var grams = recipe.Ingredients.Sum(i => i.AmountGrams);
        var portions = Math.Max(1, recipe.Portions);
        return new(total, Scale(total, 1m / portions), grams, grams / portions);
    }

    public async Task<FoodItem> SaveAsync(Recipe recipe)
    {
        recipe.Name = recipe.Name.Trim();
        if (recipe.Name.Length is 0 or > 180) throw new ArgumentException("Bitte einen Rezeptnamen mit 1–180 Zeichen angeben.");
        if (recipe.Portions is < 1 or > 50) throw new ArgumentException("Portionen: 1 bis 50.");
        if (recipe.Ingredients.Count == 0) throw new ArgumentException("Bitte mindestens eine Zutat hinzufügen.");
        if (recipe.Ingredients.Any(i => i.AmountGrams is <= 0 or > 10000)) throw new ArgumentException("Jede Zutat braucht eine Menge von 1 bis 10.000 g.");

        var nutrition = Calculate(recipe);
        var existing = await storage.GetAsync<FoodItem>(StorageStores.FoodItems, recipe.Id);
        var food = new FoodItem
        {
            Id = recipe.Id,
            Name = recipe.Name,
            Brand = "Rezept",
            Source = FoodSource.Own,
            IsFavorite = existing?.IsFavorite ?? false,
            Note = $"{recipe.Ingredients.Count} Zutaten, {recipe.Portions} Portionen",
            NutrientsPer100G = Scale(nutrition.Total, 100m / nutrition.TotalGrams),
            PortionSizes = [new PortionSize { Name = "1 Portion", Grams = Math.Round(nutrition.GramsPerPortion, 1) }]
        };
        await storage.PutAsync(StorageStores.Recipes, recipe);
        await storage.PutAsync(StorageStores.FoodItems, food);
        return food;
    }

    // Logged meals keep their own snapshot, so deleting the recipe leaves the history intact.
    public async Task DeleteAsync(Guid id)
    {
        await storage.DeleteAsync(StorageStores.Recipes, id);
        await storage.DeleteAsync(StorageStores.FoodItems, id);
    }

    private static Nutrients Scale(Nutrients n, decimal factor) => new()
    {
        Kcal = n.Kcal * factor, ProteinGrams = n.ProteinGrams * factor, CarbohydrateGrams = n.CarbohydrateGrams * factor,
        FatGrams = n.FatGrams * factor, FiberGrams = n.FiberGrams * factor, SugarGrams = n.SugarGrams * factor, SaltGrams = n.SaltGrams * factor
    };
}
