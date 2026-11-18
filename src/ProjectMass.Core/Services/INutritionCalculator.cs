using ProjectMass.Models;

namespace ProjectMass.Services;

public sealed record NutritionTargets(decimal Bmr, decimal MaintenanceKcal, decimal Kcal,
    decimal ProteinGrams, decimal CarbohydrateGrams, decimal FatGrams, bool HasInsufficientCalories);

public interface INutritionCalculator
{
    decimal CalculateBmr(Gender gender, decimal weightKg, decimal heightCm, int age);
    decimal ActivityFactor(ActivityLevel activity);
    NutritionTargets CalculateTargets(Gender gender, decimal weightKg, decimal heightCm, int age, ActivityLevel activity, FitnessGoal goal);
    Nutrients ForAmount(Nutrients per100G, decimal grams);
    Nutrients Sum(IEnumerable<Nutrients> values);
}
