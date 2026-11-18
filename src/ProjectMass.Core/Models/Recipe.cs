namespace ProjectMass.Models;

public sealed class Recipe
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public List<RecipeIngredient> Ingredients { get; set; } = [];
    public int Portions { get; set; } = 1;
    public string Instructions { get; set; } = string.Empty;
}

public sealed class RecipeIngredient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public FoodItem FoodSnapshot { get; set; } = new();
    public decimal AmountGrams { get; set; }
}
