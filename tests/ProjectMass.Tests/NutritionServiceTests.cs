using ProjectMass.Models;
using ProjectMass.Services;

namespace ProjectMass.Tests;

public class NutritionServiceTests
{
    private readonly InMemoryStorageService storage = new();
    private readonly NutritionCalculator calculator = new();
    private readonly TestClock clock = new();
    private IFoodService Foods => new FoodService(storage);
    private INutritionService Nutrition => new NutritionService(storage, Foods, calculator, clock);
    private readonly DateOnly date = new(2026, 11, 16);
    private async Task<FoodItem> Food(string name = "Haferflocken")
    {
        var food = new FoodItem { Name = name, NutrientsPer100G = new() { Kcal = 400, ProteinGrams = 20, CarbohydrateGrams = 60, FatGrams = 8 }, PortionSizes = [new() { Name = "Schüssel", Grams = 75 }] };
        await Foods.SaveAsync(food); return food;
    }

    [Fact]
    public async Task GroupsDailyTotalsByAllFourMealsAndExcludesOtherDates()
    {
        var food = await Food();
        await Nutrition.AddAsync(date, MealType.Breakfast, food.Id, 50);
        await Nutrition.AddAsync(date, MealType.Breakfast, food.Id, 25);
        await Nutrition.AddAsync(date, MealType.Lunch, food.Id, 100);
        await Nutrition.AddAsync(date.AddDays(-1), MealType.Dinner, food.Id, 500);
        var day = await Nutrition.GetDayAsync(date);
        Assert.Equal(4, day.Meals.Count);
        Assert.Equal(300, day.Meals[0].Total.Kcal);
        Assert.Equal(15, day.Meals[0].Total.ProteinGrams);
        Assert.Equal(400, day.Meals[1].Total.Kcal);
        Assert.Equal(new Nutrients(), day.Meals[2].Total);
        Assert.Equal(new Nutrients(), day.Meals[3].Total);
        Assert.Equal(700, day.Total.Kcal);
        Assert.Equal(35, day.Total.ProteinGrams);
        Assert.Equal(105, day.Total.CarbohydrateGrams);
        Assert.Equal(14, day.Total.FatGrams);
    }

    [Fact]
    public async Task EditsFavoritesAndCatalogDeletionNeverChangeLogSnapshots()
    {
        var food = await Food();
        await Nutrition.AddAsync(date, MealType.Snack, food.Id, 50);
        food.Name = "Neue Rezeptur"; food.NutrientsPer100G.Kcal = 800;
        await Foods.SaveAsync(food); await Foods.SetFavoriteAsync(food.Id, true);
        var entry = Assert.Single((await Nutrition.GetDayAsync(date)).Meals[3].Entries);
        Assert.Equal("Haferflocken", entry.FoodSnapshot.Name);
        Assert.False(entry.FoodSnapshot.IsFavorite);
        entry.FoodSnapshot.NutrientsPer100G.Kcal = 999; // Tampered input must not replace history.
        entry.AmountGrams = 75; entry.Meal = MealType.Lunch;
        await Nutrition.UpdateAsync(entry);
        await Foods.DeleteAsync(food.Id);
        var day = await Nutrition.GetDayAsync(date);
        Assert.Equal(300, day.Total.Kcal);
        Assert.Single(day.Meals[1].Entries);
        Assert.Empty(await Foods.GetRecentAsync());
        await Nutrition.DeleteAsync(entry.Id);
        Assert.Equal(0, (await Nutrition.GetDayAsync(date)).Total.Kcal);
    }

    [Fact]
    public async Task PreviousDayCopyAppendsOnlySelectedMealWithIndependentIdsAndSnapshots()
    {
        var food = await Food();
        await Nutrition.AddAsync(date.AddDays(-1), MealType.Lunch, food.Id, 75);
        await Nutrition.AddAsync(date.AddDays(-1), MealType.Dinner, food.Id, 100);
        await Nutrition.AddAsync(date, MealType.Lunch, food.Id, 25);
        await Foods.DeleteAsync(food.Id);
        var source = Assert.Single((await Nutrition.GetDayAsync(date.AddDays(-1))).Meals[1].Entries);
        Assert.Equal(1, await Nutrition.CopyPreviousMealAsync(date, MealType.Lunch));
        var day = await Nutrition.GetDayAsync(date);
        Assert.Equal(2, day.Meals[1].Entries.Count);
        Assert.Equal(400, day.Total.Kcal);
        Assert.Empty(day.Meals[2].Entries);
        var copy = day.Meals[1].Entries.Single(e => e.AmountGrams == 75);
        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal(date, copy.Date);
        Assert.Equal(source.FoodSnapshot.Id, copy.FoodSnapshot.Id);
        copy.AmountGrams = 200;
        await Nutrition.UpdateAsync(copy);
        Assert.Equal(75, (await Nutrition.GetAsync(source.Id))!.AmountGrams);
        Assert.Equal(0, await Nutrition.CopyPreviousMealAsync(date, MealType.Breakfast));
    }

    [Fact]
    public async Task RecentlyUsedIsDistinctNewestFirstLimitedToFifteenAndUsesCurrentCatalog()
    {
        var foods = new List<FoodItem>();
        for (var i = 0; i < 17; i++)
        {
            var food = await Food($"Produkt {i}"); foods.Add(food);
            await Nutrition.AddAsync(date, MealType.Snack, food.Id, 10);
            clock.Advance();
        }
        foods[0].Name = "Aktueller Name"; await Foods.SaveAsync(foods[0]);
        // Backdated logging is still the most recent use.
        await Nutrition.AddAsync(date.AddDays(-10), MealType.Snack, foods[0].Id, 20);
        var recent = await Foods.GetRecentAsync();
        Assert.Equal(15, recent.Count);
        Assert.Equal(15, recent.Select(f => f.Id).Distinct().Count());
        Assert.Equal("Aktueller Name", recent[0].Name);
        Assert.Equal(foods[16].Id, recent[1].Id);
        Assert.DoesNotContain(recent, f => f.Id == foods[1].Id);
    }

    [Fact]
    public async Task PortionsFavoritesAndInvalidValuesAreHandled()
    {
        var food = await Food();
        Assert.Equal(112.5m, Nutrition.PortionGrams(food.PortionSizes[0], 1.5m));
        Assert.Throws<ArgumentException>(() => Nutrition.PortionGrams(food.PortionSizes[0], 0));
        await Foods.SetFavoriteAsync(food.Id, true);
        Assert.Single(await Foods.SearchAsync("HAFER", true));
        Assert.Empty(await Foods.SearchAsync("unbekannt", true));
        await Assert.ThrowsAsync<ArgumentException>(() => Nutrition.AddAsync(date, MealType.Snack, food.Id, 0));
        food.NutrientsPer100G.Kcal = -1;
        await Assert.ThrowsAsync<ArgumentException>(() => Foods.SaveAsync(food));
    }

    [Fact]
    public async Task QuickEntryUsesExactDailyTotalsWithoutCreatingCatalogFood()
    {
        await Nutrition.SaveQuickEntryAsync(date, MealType.Lunch, "  ", 375.5m, 32.25m);
        var day = await Nutrition.GetDayAsync(date);
        Assert.Equal(new Nutrients { Kcal = 375.5m, ProteinGrams = 32.25m }, day.Total);
        var entry = Assert.Single(day.Meals.Single(meal => meal.Meal == MealType.Lunch).Entries);
        Assert.True(entry.IsQuickEntry);
        Assert.Equal("Schnelleintrag", entry.FoodSnapshot.Name);
        Assert.Empty(await storage.GetAllAsync<FoodItem>(StorageStores.FoodItems));
        Assert.Empty(await Foods.GetRecentAsync());

        Assert.Equal(1, await Nutrition.CopyPreviousMealAsync(date.AddDays(1), MealType.Lunch));
        await Nutrition.SaveQuickEntryAsync(date, MealType.Dinner, "  Außer Haus ", 400m, 0m, entry.Id);
        var updated = (await Nutrition.GetAsync(entry.Id))!;
        Assert.Equal("Außer Haus", updated.FoodSnapshot.Name);
        Assert.Equal(MealType.Dinner, updated.Meal);
        Assert.Equal(400m, (await Nutrition.GetDayAsync(date)).Total.Kcal);
        Assert.Equal(375.5m, (await Nutrition.GetDayAsync(date.AddDays(1))).Total.Kcal);
    }

    [Fact]
    public async Task InvalidQuickEntriesAreRejectedBeforeWriting()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Nutrition.SaveQuickEntryAsync(date, MealType.Snack, null, -1m, 0m));
        await Assert.ThrowsAsync<ArgumentException>(() => Nutrition.SaveQuickEntryAsync(date, MealType.Snack, null, 1m, null));
        await Assert.ThrowsAsync<ArgumentException>(() => Nutrition.SaveQuickEntryAsync(date, (MealType)99, null, 1m, 0m));
        Assert.Empty(await storage.GetAllAsync<FoodLogEntry>(StorageStores.FoodLogEntries));
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance() => now = now.AddSeconds(1);
    }
}
