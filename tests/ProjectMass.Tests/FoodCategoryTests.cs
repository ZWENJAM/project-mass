using System.Text.Json;
using ProjectMass.Models;
using ProjectMass.Services;

namespace ProjectMass.Tests;

public class FoodCategoryTests
{
    [Theory]
    [InlineData("Skyr mit Beeren", FoodCategory.Dairy)]
    [InlineData("Whey-Shake (30 g, Wasser)", FoodCategory.Drink)]
    [InlineData("Hähnchenbrustfilet", FoodCategory.Meat)]
    [InlineData("Schweinenacken", FoodCategory.Meat)]
    [InlineData("Thunfisch in Wasser", FoodCategory.Fish)]
    [InlineData("Eier Freilandhaltung", FoodCategory.Egg)]
    [InlineData("Basmati Reis", FoodCategory.Grain)]
    [InlineData("Haferflocken kernig", FoodCategory.Grain)]
    [InlineData("Banane", FoodCategory.Fruit)]
    [InlineData("Brokkoli TK", FoodCategory.Vegetable)]
    [InlineData("Dark Chocolate 70 %", FoodCategory.Unknown)]
    [InlineData("Zartbitter Schokolade", FoodCategory.Sweets)]
    [InlineData("Erdnussbutter", FoodCategory.Nuts)]
    [InlineData("Vanille Eis", FoodCategory.Sweets)]
    [InlineData("Wassermelone", FoodCategory.Fruit)]
    [InlineData("", FoodCategory.Unknown)]
    public void CategoryIsDetectedFromGermanNames(string name, FoodCategory expected) =>
        Assert.Equal(expected, FoodCategories.FromName(name));

    [Fact]
    public void StoredCategoryWinsAndRecipesAreMeals()
    {
        Assert.Equal(FoodCategory.Fish, FoodCategories.Resolve(new FoodItem { Name = "Hähnchen", Category = FoodCategory.Fish }));
        Assert.Equal(FoodCategory.Meal, FoodCategories.Resolve(new FoodItem { Name = "Bowl", Brand = "Rezept" }));
    }

    [Fact]
    public void OpenFoodFactsTagsAreMapped()
    {
        Assert.Equal(FoodCategory.Dairy, FoodCategories.FromOffTags(["en:dairies", "en:fermented-milk-products", "en:yogurts"]));
        Assert.Equal(FoodCategory.Unknown, FoodCategories.FromOffTags(["en:something-else"]));

        var product = JsonDocument.Parse("""
            { "code": "1", "product_name": "Skyr", "nutriments": { "energy-kcal_100g": 63 }, "categories_tags": ["en:dairies", "en:yogurts"] }
            """).RootElement;
        Assert.Equal(FoodCategory.Dairy, OpenFoodFactsProvider.Map(product)!.Category);
    }
}
