using ProjectMass.Models;
using ProjectMass.Services;

namespace ProjectMass.Tests;

public class RecipeWaterNoteTests
{
    private readonly InMemoryStorageService storage = new();
    private readonly MovingClock clock = new();
    private RecipeService Recipes => new(storage, new NutritionCalculator());
    private WaterService Water => new(storage, clock);
    private NoteService Notes => new(storage);

    private static FoodItem Food(string name, decimal kcal, decimal protein) =>
        new() { Name = name, NutrientsPer100G = new() { Kcal = kcal, ProteinGrams = protein } };

    private static Recipe Bowl() => new()
    {
        Name = "  Hähnchen-Reis-Bowl ", Portions = 2,
        Ingredients =
        [
            new RecipeIngredient { FoodSnapshot = Food("Hähnchen", 110, 23), AmountGrams = 400 }, // 440 kcal, 92 g P
            new RecipeIngredient { FoodSnapshot = Food("Reis gekocht", 130, 2.5m), AmountGrams = 400 } // 520 kcal, 10 g P
        ]
    };

    [Fact]
    public void RecipeTotalsAndPortionsAreCalculated()
    {
        var n = Recipes.Calculate(Bowl());
        Assert.Equal(960m, n.Total.Kcal);
        Assert.Equal(102m, n.Total.ProteinGrams);
        Assert.Equal(480m, n.PerPortion.Kcal);
        Assert.Equal(800m, n.TotalGrams);
        Assert.Equal(400m, n.GramsPerPortion);
    }

    [Fact]
    public async Task SavedRecipeBecomesLoggableFoodWithPortion()
    {
        var recipe = Bowl();
        var food = await Recipes.SaveAsync(recipe);
        Assert.Equal(recipe.Id, food.Id);
        Assert.Equal("Hähnchen-Reis-Bowl", food.Name);
        Assert.Equal(120m, food.NutrientsPer100G.Kcal); // 960 kcal / 800 g × 100
        Assert.Equal(12.75m, food.NutrientsPer100G.ProteinGrams);
        Assert.Equal(400m, Assert.Single(food.PortionSizes).Grams);
        Assert.NotNull(await storage.GetAsync<FoodItem>(StorageStores.FoodItems, recipe.Id));

        await Recipes.DeleteAsync(recipe.Id);
        Assert.Null(await storage.GetAsync<FoodItem>(StorageStores.FoodItems, recipe.Id));
        Assert.Empty(await Recipes.GetAllAsync());
    }

    [Fact]
    public async Task ResavingKeepsFavoriteFlag()
    {
        var recipe = Bowl();
        await Recipes.SaveAsync(recipe);
        var food = (await storage.GetAsync<FoodItem>(StorageStores.FoodItems, recipe.Id))!;
        food.IsFavorite = true;
        await storage.PutAsync(StorageStores.FoodItems, food);
        Assert.True((await Recipes.SaveAsync(recipe)).IsFavorite);
    }

    [Fact]
    public async Task InvalidRecipesAreRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Recipes.SaveAsync(new Recipe { Name = "Leer", Portions = 1 }));
        var zero = Bowl(); zero.Ingredients[0].AmountGrams = 0;
        await Assert.ThrowsAsync<ArgumentException>(() => Recipes.SaveAsync(zero));
        var portions = Bowl(); portions.Portions = 0;
        await Assert.ThrowsAsync<ArgumentException>(() => Recipes.SaveAsync(portions));
    }

    [Fact]
    public async Task WaterIsSummedPerDayAndLastEntryCanBeUndone()
    {
        var day = new DateOnly(2026, 11, 16);
        await Water.AddAsync(day, 250);
        clock.Advance();
        await Water.AddAsync(day, 500);
        await Water.AddAsync(day.AddDays(1), 1000);
        Assert.Equal(750, await Water.GetTotalMlAsync(day));

        Assert.True(await Water.RemoveLastAsync(day));
        Assert.Equal(250, await Water.GetTotalMlAsync(day));
        Assert.True(await Water.RemoveLastAsync(day));
        Assert.False(await Water.RemoveLastAsync(day));
        Assert.Equal(1000, await Water.GetTotalMlAsync(day.AddDays(1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5001)]
    public async Task WaterAmountsOutsideRangeAreRejected(int ml) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Water.AddAsync(new DateOnly(2026, 11, 16), ml));

    [Fact]
    public async Task NotesAreTrimmedSortedAndEmptyOnesRejected()
    {
        await Notes.SaveAsync(new Note { Date = new(2026, 11, 1), Text = " alt " });
        await Notes.SaveAsync(new Note { Date = new(2026, 11, 10), Text = "neu" });
        await Assert.ThrowsAsync<ArgumentException>(() => Notes.SaveAsync(new Note { Text = "   " }));
        var all = await Notes.GetAllAsync();
        Assert.Equal(new[] { "neu", "alt" }, all.Select(n => n.Text).ToArray());
    }

    private sealed class MovingClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance() => now = now.AddMinutes(1);
    }
}
