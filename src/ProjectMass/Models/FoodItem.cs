namespace ProjectMass.Models;

public enum FoodSource { Own, OpenFoodFacts, Usda }

public enum FoodCategory { Unknown, Dairy, Meat, Fish, Egg, Grain, Fruit, Vegetable, Drink, Sweets, Nuts, Meal }

// All nutrient values on FoodItem are per 100 g. Calculations belong in services.
public sealed class FoodItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string? ExternalId { get; set; }
    public string? ImageUrl { get; set; }
    public FoodCategory Category { get; set; }
    public bool HasIncompleteNutrients { get; set; }
    public FoodSource Source { get; set; } = FoodSource.Own;
    public bool IsFavorite { get; set; }
    public string Note { get; set; } = string.Empty;
    public Nutrients NutrientsPer100G { get; set; } = new();
    public List<PortionSize> PortionSizes { get; set; } = [];
}

public sealed record Nutrients
{
    public decimal Kcal { get; set; }
    public decimal ProteinGrams { get; set; }
    public decimal CarbohydrateGrams { get; set; }
    public decimal FatGrams { get; set; }
    public decimal FiberGrams { get; set; }
    public decimal SugarGrams { get; set; }
    public decimal SaltGrams { get; set; }
}

public sealed record PortionSize
{
    public string Name { get; set; } = string.Empty;
    public decimal Grams { get; set; }
}
