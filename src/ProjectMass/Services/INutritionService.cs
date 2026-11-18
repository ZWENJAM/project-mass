using ProjectMass.Models;

namespace ProjectMass.Services;

public sealed record MealSummary(MealType Meal, List<FoodLogEntry> Entries, Nutrients Total);
public sealed record NutritionDay(DateOnly Date, List<MealSummary> Meals, Nutrients Total);

public interface INutritionService
{
    Task<NutritionDay> GetDayAsync(DateOnly date);
    Task<FoodLogEntry?> GetAsync(Guid id);
    Task AddAsync(DateOnly date, MealType meal, Guid foodId, decimal grams);
    Task SaveQuickEntryAsync(DateOnly date, MealType meal, string? name, decimal? kcal, decimal? protein, Guid? id = null);
    Task UpdateAsync(FoodLogEntry entry);
    Task DeleteAsync(Guid id);
    Task<int> CopyPreviousMealAsync(DateOnly date, MealType meal);
    decimal PortionGrams(PortionSize portion, decimal count);
}
